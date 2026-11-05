using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Seam over <see cref="SoundCloudEngagementClient"/> so the cached provider's fetch can be
    /// counted and gated in tests. <see langword="null"/> means the counts are unavailable.
    /// </summary>
    internal interface ISoundCloudEngagementClient
    {
        Task<IReadOnlyList<MixEngagement>?> FetchAllAsync(long userId, CancellationToken cancellationToken);
    }
}
