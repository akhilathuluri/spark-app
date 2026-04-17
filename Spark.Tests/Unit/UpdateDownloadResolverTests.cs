using Spark.Services.Updates;
using Xunit;

namespace Spark.Tests.Unit;

public class UpdateDownloadResolverTests
{
    [Fact]
    public void Resolve_SelectsExactRuntimeMatch_WhenAvailable()
    {
        var resolver = new UpdateDownloadResolver();
        var options = new[]
        {
            new UpdateDownloadOption("win-x64", "https://example.com/x64.exe", "hash-x64"),
            new UpdateDownloadOption("win-arm64", "https://example.com/arm64.exe", "hash-arm64")
        };

        var result = resolver.Resolve("win-arm64", options, fallbackUrl: "https://example.com/default.exe");

        Assert.Equal("https://example.com/arm64.exe", result.Url);
        Assert.Equal("hash-arm64", result.Sha256);
        Assert.Equal("win-arm64", result.SelectedRuntimeId);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public void Resolve_UsesArm64ToX64Fallback_WhenArm64Missing()
    {
        var resolver = new UpdateDownloadResolver();
        var options = new[]
        {
            new UpdateDownloadOption("win-x64", "https://example.com/x64.exe", "hash-x64")
        };

        var result = resolver.Resolve("win-arm64", options, fallbackUrl: string.Empty);

        Assert.Equal("https://example.com/x64.exe", result.Url);
        Assert.Equal("hash-x64", result.Sha256);
        Assert.Equal("win-x64", result.SelectedRuntimeId);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public void Resolve_UsesGenericFallbackUrl_WhenNoRuntimeMatch()
    {
        var resolver = new UpdateDownloadResolver();
        var options = new[]
        {
            new UpdateDownloadOption("win-arm64", "https://example.com/arm64.exe", "hash-arm64")
        };

        var result = resolver.Resolve(
            "win-x64",
            options,
            fallbackUrl: "https://example.com/default.exe",
            fallbackSha256: "hash-default");

        Assert.Equal("https://example.com/default.exe", result.Url);
        Assert.Equal("hash-default", result.Sha256);
        Assert.Equal("win-x64", result.SelectedRuntimeId);
        Assert.True(result.UsedFallback);
    }

    [Theory]
    [InlineData("x64", "win-x64")]
    [InlineData("amd64", "win-x64")]
    [InlineData("arm64", "win-arm64")]
    [InlineData("win-arm64", "win-arm64")]
    [InlineData("", "win-x64")]
    public void NormalizeRuntimeId_ReturnsExpectedValues(string input, string expected)
    {
        var normalized = UpdateDownloadResolver.NormalizeRuntimeId(input);
        Assert.Equal(expected, normalized);
    }
}
