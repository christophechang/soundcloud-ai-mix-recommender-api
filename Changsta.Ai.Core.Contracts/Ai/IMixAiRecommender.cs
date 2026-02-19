using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Contracts.Ai
{
    public interface IMixAiRecommender
    {
        Task<IReadOnlyList<MixAiRecommendation>> RecommendAsync(
            string question,
            IReadOnlyList<Mix> mixes,
            int maxResults,
            CancellationToken cancellationToken);
    }
}