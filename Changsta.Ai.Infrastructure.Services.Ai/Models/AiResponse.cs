namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class SemanticKernelMixAiRecommender
    {
        private sealed class AiResponse
        {
            public List<AiResult> Results { get; set; } = new();
            public string? ClarifyingQuestion { get; set; }
        }
    }
}