using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Core.Parsing;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
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

        private readonly IMixCatalogueProvider _innerProvider;
        private readonly IBlobMixCatalogueRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly ILogger<BlobBackedMixCatalogueProvider> _logger;
        private readonly IReadOnlyDictionary<string, double> _moodWeights;
        private readonly IMoodWeightEnrichmentRepository? _enrichmentRepository;
        private readonly IMoodWeightEnricher? _moodWeightEnricher;

        public BlobBackedMixCatalogueProvider(
            IMixCatalogueProvider innerProvider,
            IBlobMixCatalogueRepository repository,
            IMemoryCache cache,
            ICatalogCacheInvalidator invalidator,
            ILogger<BlobBackedMixCatalogueProvider> logger,
            IReadOnlyDictionary<string, double> moodWeights,
            IMoodWeightEnrichmentRepository? enrichmentRepository = null,
            IMoodWeightEnricher? moodWeightEnricher = null)
        {
            _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _moodWeights = moodWeights ?? throw new ArgumentNullException(nameof(moodWeights));
            _enrichmentRepository = enrichmentRepository;
            _moodWeightEnricher = moodWeightEnricher;
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
            string cacheKey = CacheKeyPrefix + _invalidator.Version;

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Mix>? cached) && cached is not null)
            {
                return cached.Count > maxItems ? cached.Take(maxItems).ToArray() : cached;
            }

            await _loadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Re-check after acquiring the semaphore — a concurrent request may have already loaded.
                if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                {
                    return cached.Count > maxItems ? cached.Take(maxItems).ToArray() : cached;
                }

                bool blobReadSucceeded = true;
                IReadOnlyList<Mix> blobMixes;
                string? blobETag = null;

                try
                {
                    CatalogReadResult blobRead = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
                    blobMixes = blobRead.Mixes;
                    blobETag = blobRead.ETag;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CatalogueMetrics.BlobReadFailures.Add(1);
                    _logger.LogError(ex, "Blob catalog read failed — serving RSS-only catalog and skipping write-back to avoid overwriting intact data.");
                    blobMixes = Array.Empty<Mix>();
                    blobReadSucceeded = false;
                }

                bool blobGenresChanged;
                blobMixes = NormalizeGenres(blobMixes, out blobGenresChanged);

                IReadOnlyList<Mix> rssMixes = await FetchRssSafeAsync(cancellationToken)
                    .ConfigureAwait(false);
                rssMixes = NormalizeGenres(rssMixes, out _);

                LogUnknownGenres(_logger, blobMixes, rssMixes);

                IReadOnlyList<Mix> merged = MixCatalogueMerger.Merge(blobMixes, rssMixes);

                bool introHydrationChanged;
                merged = HydrateIntros(merged, out introHydrationChanged);

                bool relatedMixesChanged;
                merged = RelatedMixScorer.ComputeRelatedMixes(merged, out relatedMixesChanged);

                IReadOnlyDictionary<string, double> enrichedWeights =
                    await LoadEnrichedWeightsSafeAsync(cancellationToken).ConfigureAwait(false);
                IReadOnlyDictionary<string, double> effectiveWeights = MergeWeights(_moodWeights, enrichedWeights);

                IReadOnlyList<string> unknownMoods = FindUnknownMoods(merged, effectiveWeights);
                if (unknownMoods.Count > 0 && _moodWeightEnricher is not null)
                {
                    _logger.LogInformation(
                        "Enriching {Count} unknown mood(s) via AI: {Moods}",
                        unknownMoods.Count,
                        string.Join(", ", unknownMoods));

                    IReadOnlyDictionary<string, double> newScores =
                        await EnrichMoodsSafeAsync(effectiveWeights, unknownMoods, cancellationToken).ConfigureAwait(false);

                    if (newScores.Count > 0)
                    {
                        enrichedWeights = MergeWeights(enrichedWeights, newScores);
                        effectiveWeights = MergeWeights(_moodWeights, enrichedWeights);

                        if (_enrichmentRepository is not null)
                        {
                            try
                            {
                                await _enrichmentRepository.WriteAsync(enrichedWeights, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                _logger.LogWarning(ex, "Failed to persist enriched mood weights — continuing with in-memory scores for this catalog load.");
                            }
                        }
                    }
                }

                bool warmthChanged;
                merged = WarmthScorer.ComputeWarmth(merged, effectiveWeights, _logger, out warmthChanged);

                int newDiscoveries = MixCatalogueMerger.CountNewDiscoveries(blobMixes, rssMixes);
                int updatedEntries = MixCatalogueMerger.CountUpdatedEntries(blobMixes, rssMixes);

                if (blobReadSucceeded && (newDiscoveries > 0 || updatedEntries > 0 || blobGenresChanged || introHydrationChanged || relatedMixesChanged || warmthChanged))
                {
                    _logger.LogInformation(
                        "Writing blob catalog — {NewCount} new mixes, {UpdateCount} updated entries, genreNormalizationChanged={GenreNormalizationChanged}.",
                        newDiscoveries,
                        updatedEntries,
                        blobGenresChanged);

                    try
                    {
                        await _repository.WriteAsync(merged, blobETag, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Includes CatalogConcurrencyException: a concurrent writer changed the blob
                        // between our read and write. Serve the in-memory merged result and let the
                        // next refresh re-read and retry persistence.
                        CatalogueMetrics.BlobWriteFailures.Add(1);
                        _logger.LogWarning(ex, "Blob catalog write failed — serving the refreshed in-memory catalog and retrying persistence on the next load.");
                    }
                }

                _cache.Set(cacheKey, merged, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl,
                });

                return merged.Count > maxItems ? merged.Take(maxItems).ToArray() : merged;
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }

        public void Dispose() => _loadSemaphore.Dispose();

        private static void LogUnknownGenres(
            ILogger<BlobBackedMixCatalogueProvider> logger,
            params IReadOnlyList<Mix>[] catalogues)
        {
            var unknownGenres = catalogues
                .SelectMany(c => c)
                .Select(m => m.Genre)
                .Where(g => !string.IsNullOrWhiteSpace(g) && !GenreNormalizer.IsKnownGenre(g))
                .Select(g => GenreNormalizer.Normalize(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string genre in unknownGenres)
            {
                logger.LogWarning("Unknown genre value found during catalog sync: {Genre}", genre);
            }
        }

        private static IReadOnlyList<Mix> NormalizeGenres(IReadOnlyList<Mix> mixes, out bool changed)
        {
            changed = false;
            var normalized = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];
                string genre = GenreNormalizer.Normalize(mix.Genre);

                if (!string.Equals(mix.Genre, genre, StringComparison.Ordinal))
                {
                    changed = true;
                    normalized[i] = mix with { Genre = genre };
                }
                else
                {
                    normalized[i] = mix;
                }
            }

            return normalized;
        }

        private static IReadOnlyList<Mix> HydrateIntros(IReadOnlyList<Mix> mixes, out bool changed)
        {
            changed = false;
            var result = new Mix[mixes.Count];

            for (int i = 0; i < mixes.Count; i++)
            {
                Mix mix = mixes[i];

                if (mix.Intro is not null)
                {
                    result[i] = mix;
                    continue;
                }

                string? intro = MixDescriptionParser.ExtractIntro(mix.Description);

                if (intro is null)
                {
                    result[i] = mix;
                    continue;
                }

                changed = true;
                result[i] = mix with { Intro = intro };
            }

            return result;
        }

        private static IReadOnlyDictionary<string, double> MergeWeights(
            IReadOnlyDictionary<string, double> baseWeights,
            IReadOnlyDictionary<string, double> additions)
        {
            var merged = new Dictionary<string, double>(baseWeights, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in additions)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        private static IReadOnlyList<string> FindUnknownMoods(
            IReadOnlyList<Mix> mixes,
            IReadOnlyDictionary<string, double> weights)
        {
            return mixes
                .SelectMany(m => m.Moods)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !weights.ContainsKey(m))
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<IReadOnlyList<Mix>> FetchRssSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _innerProvider
                    .GetLatestAsync(int.MaxValue, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.RssFetchFailures.Add(1);
                _logger.LogWarning(ex, "RSS feed fetch failed — proceeding with blob catalog only.");
                return Array.Empty<Mix>();
            }
        }

        private async Task<IReadOnlyDictionary<string, double>> LoadEnrichedWeightsSafeAsync(
            CancellationToken cancellationToken)
        {
            if (_enrichmentRepository is null)
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return await _enrichmentRepository.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.EnrichedWeightsLoadFailures.Add(1);
                _logger.LogWarning(ex, "Failed to load enriched mood weights — proceeding with base weights only.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task<IReadOnlyDictionary<string, double>> EnrichMoodsSafeAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> unknownMoods,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _moodWeightEnricher!
                    .EnrichAsync(existingWeights, unknownMoods, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.AiEnrichmentFailures.Add(1);
                _logger.LogWarning(ex, "AI mood enrichment failed — new moods will have no weight this cycle.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
