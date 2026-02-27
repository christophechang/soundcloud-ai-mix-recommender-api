namespace Changsta.Ai.Core.Domain
{
    public sealed class MixAiRecommendation
    {
        required public string MixId { get; init; }

        required public string Reason { get; init; }

        required public IReadOnlyList<string> Why { get; init; }

        public double Confidence { get; init; }

        required public string Title { get; init; }

        required public string Url { get; init; }
    }
}