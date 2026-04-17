namespace Spark.Services.Updates;

public sealed record UpdateDownloadOption(
    string RuntimeId,
    string Url,
    string Sha256 = "");

public sealed record UpdateDownloadResolution(
    string Url,
    string Sha256,
    string SelectedRuntimeId,
    bool UsedFallback);

public sealed class UpdateDownloadResolver
{
    public UpdateDownloadResolution Resolve(
        string preferredRuntimeId,
        IEnumerable<UpdateDownloadOption> runtimeOptions,
        string fallbackUrl,
        string fallbackSha256 = "")
    {
        var normalizedPreferred = NormalizeRuntimeId(preferredRuntimeId);
        var byRuntime = runtimeOptions
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .GroupBy(x => NormalizeRuntimeId(x.RuntimeId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        if (byRuntime.TryGetValue(normalizedPreferred, out var exact))
        {
            return new UpdateDownloadResolution(
                exact.Url,
                exact.Sha256,
                normalizedPreferred,
                UsedFallback: false);
        }

        // Windows Arm64 can execute x64 binaries under emulation.
        if (string.Equals(normalizedPreferred, "win-arm64", StringComparison.OrdinalIgnoreCase)
            && byRuntime.TryGetValue("win-x64", out var armFallback))
        {
            return new UpdateDownloadResolution(
                armFallback.Url,
                armFallback.Sha256,
                "win-x64",
                UsedFallback: true);
        }

        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            return new UpdateDownloadResolution(
                fallbackUrl,
                fallbackSha256,
                normalizedPreferred,
                UsedFallback: true);
        }

        var first = byRuntime.Values.FirstOrDefault();
        if (first is not null)
        {
            return new UpdateDownloadResolution(
                first.Url,
                first.Sha256,
                NormalizeRuntimeId(first.RuntimeId),
                UsedFallback: true);
        }

        return new UpdateDownloadResolution(
            string.Empty,
            string.Empty,
            normalizedPreferred,
            UsedFallback: true);
    }

    public static string NormalizeRuntimeId(string? runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return "win-x64";

        var token = runtimeId.Trim().ToLowerInvariant();

        return token switch
        {
            "arm64" => "win-arm64",
            "x64" => "win-x64",
            "amd64" => "win-x64",
            _ => token
        };
    }
}
