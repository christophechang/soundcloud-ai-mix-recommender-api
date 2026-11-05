using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    /// <summary>
    /// Live play and like counts for every public mix in the catalogue. Returns
    /// <see langword="null"/> when the counts are unavailable (switched off, not configured, or the
    /// upstream fetch failed); callers render nothing in that case.
    /// </summary>
    public interface IMixEngagementProvider
    {
        Task<IReadOnlyList<MixEngagement>?> GetAllAsync(CancellationToken cancellationToken);
    }
}
