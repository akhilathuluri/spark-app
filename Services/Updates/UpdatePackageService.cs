using System.IO;
using System.Net.Http;
using Spark.Models;

namespace Spark.Services.Updates;

public enum UpdatePackageFailureReason
{
    None,
    MissingUrl,
    MissingHash,
    InvalidHashFormat,
    DownloadFailed,
    HashMismatch,
    FileMoveFailed
}

public sealed class UpdatePackageOperationResult
{
    public bool Succeeded { get; init; }
    public UpdatePackageFailureReason FailureReason { get; init; } = UpdatePackageFailureReason.None;
    public string Message { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string ExpectedSha256 { get; init; } = string.Empty;
    public string ActualSha256 { get; init; } = string.Empty;

    public static UpdatePackageOperationResult Fail(UpdatePackageFailureReason reason, string message)
        => new() { Succeeded = false, FailureReason = reason, Message = message };

    public static UpdatePackageOperationResult Ok(string filePath, string expectedSha256, string actualSha256)
        => new()
        {
            Succeeded = true,
            FilePath = filePath,
            ExpectedSha256 = expectedSha256,
            ActualSha256 = actualSha256,
            Message = "Update package downloaded and verified."
        };
}

public sealed class UpdatePackageService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly UpdateHashVerificationService _hashVerificationService;
    private readonly string? _updatesRootPath;

    public UpdatePackageService(
        HttpClient? httpClient = null,
        UpdateHashVerificationService? hashVerificationService = null,
        string? updatesRootPath = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _hashVerificationService = hashVerificationService ?? new UpdateHashVerificationService();
        _updatesRootPath = updatesRootPath;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    public async Task<UpdatePackageOperationResult> DownloadAndVerifyAsync(
        AppUpdateInfo updateInfo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            return UpdatePackageOperationResult.Fail(
                UpdatePackageFailureReason.MissingUrl,
                "No download URL is available for this update.");
        }

        var normalizedExpectedHash = UpdateHashVerificationService.NormalizeSha256(updateInfo.DownloadSha256);
        if (string.IsNullOrWhiteSpace(normalizedExpectedHash))
        {
            return UpdatePackageOperationResult.Fail(
                UpdatePackageFailureReason.MissingHash,
                "This update does not include a SHA-256 hash, so verification cannot be completed.");
        }

        if (!UpdateHashVerificationService.IsValidSha256(normalizedExpectedHash))
        {
            return UpdatePackageOperationResult.Fail(
                UpdatePackageFailureReason.InvalidHashFormat,
                "The update SHA-256 hash format is invalid.");
        }

        var updatesDirectory = GetUpdatesDirectory();
        Directory.CreateDirectory(updatesDirectory);

        var targetFileName = BuildTargetFileName(updateInfo);
        var targetFilePath = Path.Combine(updatesDirectory, targetFileName);
        var tempFilePath = targetFilePath + ".download";

        try
        {
            using var response = await _httpClient.GetAsync(
                updateInfo.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var network = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(tempFilePath))
            {
                await network.CopyToAsync(file, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            SafeDelete(tempFilePath);
            DebugLogService.LogError("UpdatePackageService.Download", ex);

            return UpdatePackageOperationResult.Fail(
                UpdatePackageFailureReason.DownloadFailed,
                "Failed to download the update package.");
        }

        if (!_hashVerificationService.TryVerifyFile(tempFilePath, normalizedExpectedHash, out var actualHash))
        {
            SafeDelete(tempFilePath);

            return UpdatePackageOperationResult.Fail(
                UpdatePackageFailureReason.HashMismatch,
                $"SHA-256 verification failed. Expected {normalizedExpectedHash}, received {actualHash}.");
        }

        try
        {
            if (File.Exists(targetFilePath))
                File.Delete(targetFilePath);

            File.Move(tempFilePath, targetFilePath);
            return UpdatePackageOperationResult.Ok(targetFilePath, normalizedExpectedHash, actualHash);
        }
        catch (Exception ex)
        {
            SafeDelete(tempFilePath);
            DebugLogService.LogError("UpdatePackageService.MoveFile", ex);

            return UpdatePackageOperationResult.Fail(
                UpdatePackageFailureReason.FileMoveFailed,
                "The update package was downloaded but could not be finalized locally.");
        }
    }

    private string GetUpdatesDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_updatesRootPath))
            return _updatesRootPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spark",
            "Updates");
    }

    private static string BuildTargetFileName(AppUpdateInfo updateInfo)
    {
        var runtime = string.IsNullOrWhiteSpace(updateInfo.SelectedRuntimeId)
            ? updateInfo.PreferredRuntimeId
            : updateInfo.SelectedRuntimeId;
        var version = string.IsNullOrWhiteSpace(updateInfo.LatestVersion)
            ? "latest"
            : updateInfo.LatestVersion.Replace('.', '_');

        var fromUrl = TryGetFileNameFromUrl(updateInfo.DownloadUrl);
        if (!string.IsNullOrWhiteSpace(fromUrl))
            return SanitizeFileName(fromUrl);

        return SanitizeFileName($"Spark-{version}-{runtime}.exe");
    }

    private static string? TryGetFileNameFromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var name = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string SanitizeFileName(string fileName)
    {
        var cleaned = new string(fileName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Spark-update.exe";

        return cleaned;
    }

    private static void SafeDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
