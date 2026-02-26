namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class SemanticKernelMixAiRecommender
    {
        private sealed class IntentResponse
        {
            public List<string> PreferredGenres { get; set; } = new();
            public List<string> AvoidGenres { get; set; } = new();
            public List<string> PreferredEnergy { get; set; } = new();
            public int? BpmMin { get; set; }
            public int? BpmMax { get; set; }
            public List<string> RequiredMoods { get; set; } = new();
            public List<string> AvoidMoods { get; set; } = new();
            public string Notes { get; set; } = string.Empty;
        }
    }
}