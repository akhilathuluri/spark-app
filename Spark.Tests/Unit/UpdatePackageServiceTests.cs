using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Spark.Models;
using Spark.Services.Updates;
using Xunit;

namespace Spark.Tests.Unit;

public class UpdatePackageServiceTests
{
    [Fact]
    public async Task DownloadAndVerifyAsync_FailsWhenHashMissing()
    {
        var handler = new StaticHttpHandler(Encoding.UTF8.GetBytes("payload"));
        using var httpClient = new HttpClient(handler);
        using var service = new UpdatePackageService(httpClient: httpClient, updatesRootPath: CreateTempDir());

        var info = new AppUpdateInfo
        {
            DownloadUrl = "https://example.com/SparkInstaller.exe",
            DownloadSha256 = string.Empty,
            LatestVersion = "1.2.3",
            SelectedRuntimeId = "win-x64"
        };

        var result = await service.DownloadAndVerifyAsync(info);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdatePackageFailureReason.MissingHash, result.FailureReason);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_SucceedsWhenHashMatches()
    {
        var payload = Encoding.UTF8.GetBytes("spark verified payload");
        var expectedHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var tempDir = CreateTempDir();
        var handler = new StaticHttpHandler(payload);
        using var httpClient = new HttpClient(handler);
        using var service = new UpdatePackageService(httpClient: httpClient, updatesRootPath: tempDir);

        var info = new AppUpdateInfo
        {
            DownloadUrl = "https://example.com/SparkInstaller.exe",
            DownloadSha256 = expectedHash,
            LatestVersion = "1.2.3",
            SelectedRuntimeId = "win-x64"
        };

        var result = await service.DownloadAndVerifyAsync(info);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedHash, result.ExpectedSha256);
        Assert.Equal(expectedHash, result.ActualSha256);
        Assert.True(File.Exists(result.FilePath));

        var fileBytes = await File.ReadAllBytesAsync(result.FilePath);
        Assert.Equal(payload, fileBytes);
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Spark-update-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticHttpHandler(byte[] payload) : HttpMessageHandler
    {
        private readonly byte[] _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };

            return Task.FromResult(response);
        }
    }
}
