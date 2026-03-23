using Spark.Services;
using Xunit;

namespace Spark.Tests.Unit;

public class TextSanitizationServiceTests
{
    [Fact]
    public void SanitizeFinal_RemovesAnsiAndUnwrapsWholeCodeFence()
    {
        var sut = new TextSanitizationService();
        var input = "```\n\u001b[31mhello\u001b[0m\n```";

        var output = sut.SanitizeFinal(input);

        Assert.Equal("hello", output);
    }
}
