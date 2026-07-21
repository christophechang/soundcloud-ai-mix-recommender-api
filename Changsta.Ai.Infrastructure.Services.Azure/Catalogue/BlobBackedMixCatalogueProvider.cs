using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Coordinates a catalogue rebuild: cache, the rebuild lock, and the load → merge → hydrate →
    /// persist pipeline. Each stage lives in its own collaborator; this class owns only the
    /// sequencing and the caching around it.
    /// </summary>
    public sealed class BlobBackedMixCatalogueProvider : IMixCatalogueProvider, IDisposable
    {
        private const string CacheKeyPrefix = "blob_catalog_v";

        // 1-hour merged-catalogue TTL — matches the documented contract (README). Newly
        // published mixes appear within this window; the warmup service and flush endpoint
        // can refresh sooner. See issue #88.
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        // Serialises catalogue rebuilds so concurrent cold requests don't all rebuild (and re-run
        // RSS fetch / mood enrichment) at once. This is an instance field: the provider is
        // registered as a Singleton (see Program.cs), so one instance owns the lock — matching the
        // intent, without mixing static state into a per-request lifetime. See issue #56.
        private readonly SemaphoreSlim _loadSemaphore = new SemaphoreSlim(1, 1);

        private readonly IMemoryCache _cache;
        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly ICatalogueLoader _loader;
        private readonly ICatalogueHydrator _hydrator;
        private readonly ICataloguePersister _persister;

        public BlobBackedMixCatalogueProvider(
            IMixCatalogueProvider innerProvider,
            IBlobMixCatalogueRepository repository,
            IMemoryCache cache,
            ICatalogCacheInvalidator invalidator,
            ILogger<BlobBackedMixCatalogueProvider> logger,
            IReadOnlyDictionary<string, double> moodWeights,
            IMoodWeightEnrichmentRepository? enrichmentRepository = null,
            IMoodWeightEnricher? moodWeightEnricher = null)
            : this(
                cache,
                invalidator,
                new CatalogueLoader(
                    innerProvider ?? throw new ArgumentNullException(nameof(innerProvider)),
                    repository ?? throw new ArgumentNullException(nameof(repository)),
                    logger ?? throw new ArgumentNullException(nameof(logger))),
                new CatalogueHydrator(
                    new MoodWeightResolver(
                        moodWeights ?? throw new ArgumentNullException(nameof(moodWeights)),
                        enrichmentRepository ?? NullMoodWeightEnrichmentRepository.Instance,
                        moodWeightEnricher ?? NullMoodWeightEnricher.Instance,
                        logger),
                    logger),
                new CataloguePersister(repository, logger))
        {
        }

        /// <summary>
        /// Collaborator-injecting constructor, used by tests to exercise the coordination logic
        /// without standing up the whole pipeline.
        /// </summary>
        internal BlobBackedMixCatalogueProvider(
            IMemoryCache cache,
            ICatalogCacheInvalidator invalidator,
            ICatalogueLoader loader,
            ICatalogueHydrator hydrator,
            ICataloguePersister persister)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _hydrator = hydrator ?? throw new ArgumentNullException(nameof(hydrator));
            _persister = persister ?? throw new ArgumentNullException(nameof(persister));
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
            string cacheKey = CacheKeyPrefix + _invalidator.Version;

            if (TryGetCached(cacheKey, maxItems, out IReadOnlyList<Mix>? cached))
            {
                return cached;
            }

            await _loadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Re-check after acquiring the semaphore — a concurrent request may have already loaded.
                if (TryGetCached(cacheKey, maxItems, out cached))
                {
                    return cached;
                }

                CatalogueLoadResult load = await _loader.LoadAsync(cancellationToken).ConfigureAwait(false);

                IReadOnlyList<Mix> merged = MixCatalogueMerger.Merge(load.BlobMixes, load.RssMixes);

                CatalogueHydrationResult hydrated =
                    await _hydrator.HydrateAsync(merged, cancellationToken).ConfigureAwait(false);

                await _persister
                    .PersistIfChangedAsync(hydrated.Mixes, load, hydrated.Changed, cancellationToken)
                    .ConfigureAwait(false);

                _cache.Set(cacheKey, hydrated.Mixes, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl,
                });

                return Limit(hydrated.Mixes, maxItems);
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }

        public void Dispose() => _loadSemaphore.Dispose();

        private static IReadOnlyList<Mix> Limit(IReadOnlyList<Mix> mixes, int maxItems) =>
            mixes.Count > maxItems ? mixes.Take(maxItems).ToArray() : mixes;

        private bool TryGetCached(string cacheKey, int maxItems, out IReadOnlyList<Mix> mixes)
        {
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Mix>? cached) && cached is not null)
            {
                mixes = Limit(cached, maxItems);
                return true;
            }

            mixes = Array.Empty<Mix>();
            return false;
        }
    }
}
