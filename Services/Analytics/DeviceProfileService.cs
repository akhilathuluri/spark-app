using System.Diagnostics;
using Microsoft.Win32;

namespace Spark.Services.Analytics;

public sealed record DeviceProfile(
    string UserName,
    string MachineName,
    string DeviceModel,
    string DeviceDisplayName,
    string Email,
    string EmailSource)
{
    public static readonly DeviceProfile Empty = new(
        UserName: string.Empty,
        MachineName: string.Empty,
        DeviceModel: string.Empty,
        DeviceDisplayName: string.Empty,
        Email: string.Empty,
        EmailSource: string.Empty);
}

public sealed class DeviceProfileService
{
    public DeviceProfile GetCurrentProfile(bool includeEmail, string? deviceEmailOverride)
    {
        var userName = Normalize(Environment.UserName);
        var machineName = Normalize(Environment.MachineName);
        var model = GetDeviceModel();
        var displayName = BuildDisplayName(userName, model);

        var (email, source) = ResolveEmail(includeEmail, deviceEmailOverride);

        return new DeviceProfile(
            UserName: userName,
            MachineName: machineName,
            DeviceModel: model,
            DeviceDisplayName: displayName,
            Email: email,
            EmailSource: source);
    }

    private static string GetDeviceModel()
    {
        var manufacturer = ReadBiosValue("SystemManufacturer");
        var product = ReadBiosValue("SystemProductName");

        if (string.IsNullOrWhiteSpace(manufacturer) && string.IsNullOrWhiteSpace(product))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(manufacturer))
            return product;

        if (string.IsNullOrWhiteSpace(product))
            return manufacturer;

        if (product.Contains(manufacturer, StringComparison.OrdinalIgnoreCase))
            return product;

        return $"{manufacturer} {product}";
    }

    private static string ReadBiosValue(string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var value = key?.GetValue(valueName)?.ToString();
            return Normalize(value);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (string email, string source) ResolveEmail(bool includeEmail, string? deviceEmailOverride)
    {
        if (!includeEmail)
            return (string.Empty, "disabled");

        var explicitEmail = Normalize(deviceEmailOverride);
        if (LooksLikeEmail(explicitEmail))
            return (explicitEmail, "config");

        var envEmail = Normalize(Environment.GetEnvironmentVariable("Spark_ANALYTICS_DEVICE_EMAIL"));
        if (LooksLikeEmail(envEmail))
            return (envEmail, "env");

        var upnEmail = TryReadUserPrincipalName();
        if (LooksLikeEmail(upnEmail))
            return (upnEmail, "upn");

        return (string.Empty, "none");
    }

    private static string TryReadUserPrincipalName()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "whoami",
                Arguments = "/upn",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return string.Empty;

            if (!process.WaitForExit(1200))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            var output = Normalize(process.StandardOutput.ReadToEnd());
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildDisplayName(string userName, string model)
    {
        if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(model))
            return $"{userName} / {model}";

        if (!string.IsNullOrWhiteSpace(model))
            return model;

        return userName;
    }

    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var at = value.IndexOf('@');
        var dot = value.LastIndexOf('.');
        return at > 0 && dot > at + 1 && dot < value.Length - 1;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim();
    }
}
