namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class SemanticKernelMixAiRecommender
    {
        private sealed class AiResult
        {
            public required string MixId { get; init; }
            public required string Title { get; init; }
            public required string Url { get; init; }
            public required List<string> Why { get; init; }
            public double Confidence { get; init; }
        }
    }
}