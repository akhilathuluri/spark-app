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

    [Fact]
    public void ComputeFastFallback_PreservesSharedPrefixAndSuffix()
    {
        var original = "alpha beta gamma omega";
        var result = "alpha beta delta omega";

        var chunks = DiffService.ComputeFastFallback(original, result);

        Assert.True(chunks.Count >= 3);
        Assert.Equal(DiffType.Equal, chunks[0].Type);
        Assert.Contains("alpha beta", chunks[0].Text);
        Assert.Contains(chunks, c => c.Type == DiffType.Delete && c.Text.Contains("gamma"));
        Assert.Contains(chunks, c => c.Type == DiffType.Insert && c.Text.Contains("delta"));
        Assert.Equal(DiffType.Equal, chunks[^1].Type);
        Assert.Contains("omega", chunks[^1].Text);
    }
}
