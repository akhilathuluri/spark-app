using System.Security.Cryptography;
using System.Text;
using Spark.Services.Updates;
using Xunit;

namespace Spark.Tests.Unit;

public class UpdateHashVerificationServiceTests
{
    [Fact]
    public void NormalizeSha256_RemovesSeparators_AndLowercases()
    {
        var raw = "AA-BB CC";
        var normalized = UpdateHashVerificationService.NormalizeSha256(raw);
        Assert.Equal("aabbcc", normalized);
    }

    [Fact]
    public void IsValidSha256_ReturnsTrue_For64HexCharacters()
    {
        var hash = new string('a', 64);
        Assert.True(UpdateHashVerificationService.IsValidSha256(hash));
    }

    [Fact]
    public void TryVerifyFile_ReturnsTrue_ForMatchingHash()
    {
        var service = new UpdateHashVerificationService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Spark-hash-{Guid.NewGuid()}.txt");

        try
        {
            var content = "spark update payload";
            File.WriteAllText(tempFile, content, Encoding.UTF8);
            var fileBytes = File.ReadAllBytes(tempFile);
            var expectedHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

            var ok = service.TryVerifyFile(tempFile, expectedHash, out var actual);

            Assert.True(ok);
            Assert.Equal(expectedHash, actual);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
