using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Holds the whole-catalogue counts in one in-process entry stamped from
    /// <see cref="TimeProvider"/>: the configured TTL on success, 60 seconds on failure. Nothing is
    /// persisted. Concurrent callers on an expired entry share one fetch. That fetch belongs to the
    /// provider and runs under its own budget, so a caller who cancels stops only its own wait; it
    /// never cancels the fetch for the others, never writes a failure into the entry and holds no lock.
    /// </summary>
    internal sealed class CachedMixEngagementProvider : IMixEngagementProvider
    {
        private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(60);

        private readonly ISoundCloudEngagementClient _client;
        private readonly SoundCloudEngagementSettings _settings;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _fetchBudget;
        private readonly TimeSpan _successTtl;
        private readonly bool _hasUserId;
        private readonly long _userId;
        private readonly object _gate = new();

        private CacheEntry? _entry;
        private Task<IReadOnlyList<MixEngagement>?>? _inFlight;

        public CachedMixEngagementProvider(
            ISoundCloudEngagementClient client,
            SoundCloudEngagementSettings settings,
            TimeProvider timeProvider,
            TimeSpan fetchBudget)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _fetchBudget = fetchBudget;
            _successTtl = TimeSpan.FromSeconds(Math.Max(1, settings.StatsCacheSeconds));
            _hasUserId = SoundCloudRssUrl.TryParseUserId(settings.RssUrl, out _userId);
        }

        public async Task<IReadOnlyList<MixEngagement>?> GetAllAsync(CancellationToken cancellationToken)
        {
            if (!_settings.StatsEnabled || !_settings.IsConfigured || !_hasUserId)
            {
                return null;
            }

            Task<IReadOnlyList<MixEngagement>?> fetch;
            lock (_gate)
            {
                CacheEntry? entry = _entry;
                if (entry is not null && _timeProvider.GetUtcNow() < entry.ExpiresAt)
                {
                    return entry.Value;
                }

                // Task.Run so the fetch can never complete (and clear _inFlight) before it is assigned.
                _inFlight ??= Task.Run(FetchAndStoreAsync, CancellationToken.None);
                fetch = _inFlight;
            }

            return await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<MixEngagement>?> FetchAndStoreAsync()
        {
            IReadOnlyList<MixEngagement>? result;
            try
            {
                using var budget = new CancellationTokenSource(_fetchBudget);
                result = await _client.FetchAllAsync(_userId, budget.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Over budget, or anything the client did not map to null: never throw to waiters.
                result = null;
            }

            lock (_gate)
            {
                TimeSpan ttl = result is null ? FailureTtl : _successTtl;
                _entry = new CacheEntry(result, _timeProvider.GetUtcNow().Add(ttl));
                _inFlight = null;
            }

            return result;
        }

        private sealed class CacheEntry
        {
            public CacheEntry(IReadOnlyList<MixEngagement>? value, DateTimeOffset expiresAt)
            {
                Value = value;
                ExpiresAt = expiresAt;
            }

            public IReadOnlyList<MixEngagement>? Value { get; }

            public DateTimeOffset ExpiresAt { get; }
        }
    }
}
