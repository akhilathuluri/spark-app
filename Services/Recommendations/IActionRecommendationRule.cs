namespace Spark.Services.Recommendations;

public interface IActionRecommendationRule
{
    int Weight { get; }

    bool Applies(ActionRecommendationContext context);

    IReadOnlyList<string> GetActionIds(ActionRecommendationContext context);
}
