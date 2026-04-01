namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class OpenAiMixRecommender
    {
        internal sealed class AiResult
        {
            public required string MixId { get; init; }
            public string? Title { get; init; }
            public string? Url { get; init; }
            public required string Reason { get; init; }
            public required List<string> Why { get; init; }
            public double Confidence { get; init; }
        }
    }
}
