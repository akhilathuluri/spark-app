namespace Spark.Models;

public sealed class FollowUpActionOption
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IconActionId { get; init; } = string.Empty;
    public string PromptTemplate { get; init; } = string.Empty;
}
