namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    internal sealed class AiRecommendationResponse
    {
        public required List<AiRecommendationResult> Results { get; init; }
        public string? ClarifyingQuestion { get; init; }
    }
}
