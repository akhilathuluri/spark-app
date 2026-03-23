using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spark.Services;

/// <summary>
/// Stores secrets encrypted with Windows DPAPI (CurrentUser scope).
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "secrets.dat");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Spark.Secrets.v1");
    private readonly object _sync = new();

    public string? Get(string key)
    {
        try
        {
            lock (_sync)
            {
                var map = LoadMap();
                return map.TryGetValue(key, out var value) ? value : null;
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("DpapiSecretStore.Get", ex);
            return null;
        }
    }

    public void Set(string key, string value)
    {
        try
        {
            lock (_sync)
            {
                var map = LoadMap();
                map[key] = value;
                SaveMap(map);
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("DpapiSecretStore.Set", ex);
        }
    }

    public void Remove(string key)
    {
        try
        {
            lock (_sync)
            {
                var map = LoadMap();
                if (!map.Remove(key))
                    return;

                SaveMap(map);
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("DpapiSecretStore.Remove", ex);
        }
    }

    private static Dictionary<string, string> LoadMap()
    {
        if (!File.Exists(StorePath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var encrypted = File.ReadAllBytes(StorePath);
        if (encrypted.Length == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var plainBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(plainBytes);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveMap(Dictionary<string, string> map)
    {
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(map, JsonOptions);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to encrypt secrets payload with DPAPI.", ex);
        }

        var tempPath = Path.Combine(directory, ".secrets.dat.tmp");
        var backupPath = StorePath + ".bak";
        File.WriteAllBytes(tempPath, encrypted);

        if (File.Exists(StorePath))
        {
            File.Replace(tempPath, StorePath, backupPath, ignoreMetadataErrors: true);
            try { File.Delete(backupPath); } catch { }
            return;
        }

        File.Move(tempPath, StorePath);
    }
}
