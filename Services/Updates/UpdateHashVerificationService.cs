using System.IO;
using System.Security.Cryptography;

namespace Spark.Services.Updates;

public sealed class UpdateHashVerificationService
{
    public string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool TryVerifyFile(string filePath, string expectedSha256, out string actualSha256)
    {
        actualSha256 = ComputeFileSha256(filePath);
        var normalizedExpected = NormalizeSha256(expectedSha256);
        if (!IsValidSha256(normalizedExpected))
            return false;

        return string.Equals(actualSha256, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var filtered = new string(value.Trim().Where(Uri.IsHexDigit).ToArray());
        return filtered.ToLowerInvariant();
    }

    public static bool IsValidSha256(string? value)
    {
        var normalized = NormalizeSha256(value);
        if (normalized.Length != 64)
            return false;

        return normalized.All(Uri.IsHexDigit);
    }
}
