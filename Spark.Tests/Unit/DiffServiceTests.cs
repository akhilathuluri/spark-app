using Spark.Services;
using Xunit;

namespace Spark.Tests.Unit;

public class DiffServiceTests
{
    [Fact]
    public void Compute_ReturnsInsertChunk_WhenResultAddsWord()
    {
        var chunks = DiffService.Compute("hello world", "hello brave world");

        Assert.Contains(chunks, c => c.Type == DiffType.Insert && c.Text.Contains("brave"));
    }
}
