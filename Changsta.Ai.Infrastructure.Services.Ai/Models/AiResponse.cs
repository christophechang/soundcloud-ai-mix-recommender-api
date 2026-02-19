namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class SemanticKernelMixAiRecommender
    {
        private sealed class AiResponse
        {
            public required List<AiResult> Results { get; init; }
            public string? ClarifyingQuestion { get; init; }
        }
    }
}