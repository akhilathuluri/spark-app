using System.Net;
using Spark.Services;
using Xunit;

namespace Spark.Tests.Integration;

public class RetryPolicyIntegrationTests
{
    [Fact]
    public async Task RetryPolicy_RetriesOn429_ThenSucceeds()
    {
        var policy = RetryService.GetHttpRetryPolicy();
        int attempts = 0;

        var response = await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        Assert.Equal(3, attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
