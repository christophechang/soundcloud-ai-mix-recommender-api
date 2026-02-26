namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class SemanticKernelMixAiRecommender
    {
        private sealed class AiResult
        {
            public string MixId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public List<string> Why { get; set; } = new();
            public double Confidence { get; set; }
        }
    }
}