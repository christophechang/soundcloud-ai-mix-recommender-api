using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Core.Parsing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    public sealed class BlobBackedMixCatalogueProvider : IMixCatalogueProvider
    {
        private const string CacheKeyPrefix = "blob_catalog_v";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        // Intentionally static: guards a process-wide blob load and must be shared across all
        // per-request instances. The scoped lifetime of this class is for DI composition only.
        private static readonly SemaphoreSlim LoadSemaphore = new SemaphoreSlim(1, 1);

        private readonly IMixCatalogueProvider _innerProvider;
        private readonly IBlobMixCatalogueRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly ILogger<BlobBackedMixCatalogueProvider> _logger;

        public BlobBackedMixCatalogueProvider(
            IMixCatalogueProvider innerProvider,
            IBlobMixCatalogueRepository repository,
            IMemoryCache cache,
            ICatalogCacheInvalidator invalidator,
            ILogger<BlobBackedMixCatalogueProvider> logger)
        {
            _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
            string cacheKey = CacheKeyPrefix + _invalidator.Version;

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Mix>? cached) && cached is not null)
            {
                return cached.Count > maxItems ? cached.Take(maxItems).ToArray() : cached;
            }

            await LoadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Re-check after acquiring the semaphore — a concurrent request may have already loaded.
                if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                {
                    return cached.Count > maxItems ? cached.Take(maxItems).ToArray() : cached;
                }

                bool blobReadSucceeded = true;
                IReadOnlyList<Mix> blobMixes;

                try
                {
                    blobMixes = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
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

                IReadOnlyList<Mix> merged = MergeCatalogs(blobMixes, rssMixes);

                bool introHydrationChanged;
                merged = HydrateIntros(merged, out introHydrationChanged);

                int newDiscoveries = CountNewDiscoveries(blobMixes, rssMixes);
                int updatedEntries = CountUpdatedEntries(blobMixes, rssMixes);

                if (blobReadSucceeded && (newDiscoveries > 0 || updatedEntries > 0 || blobGenresChanged || introHydrationChanged))
                {
                    _logger.LogInformation(
                        "Writing blob catalog — {NewCount} new mixes, {UpdateCount} updated entries, genreNormalizationChanged={GenreNormalizationChanged}.",
                        newDiscoveries,
                        updatedEntries,
                        blobGenresChanged);

                    await _repository.WriteAsync(merged, cancellationToken).ConfigureAwait(false);
                }

                _cache.Set(cacheKey, merged, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl,
                });

                return merged.Count > maxItems ? merged.Take(maxItems).ToArray() : merged;
            }
            finally
            {
                LoadSemaphore.Release();
            }
        }

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
                    normalized[i] = WithGenre(mix, genre);
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
                result[i] = WithIntro(mix, intro);
            }

            return result;
        }

        private static Mix WithGenre(Mix mix, string genre)
        {
            return new Mix
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Description = mix.Description,
                Intro = mix.Intro,
                Duration = mix.Duration,
                ImageUrl = mix.ImageUrl,
                Tracklist = mix.Tracklist,
                Genre = genre,
                Energy = mix.Energy,
                BpmMin = mix.BpmMin,
                BpmMax = mix.BpmMax,
                Moods = mix.Moods,
                PublishedAt = mix.PublishedAt,
            };
        }

        private static Mix WithIntro(Mix mix, string? intro)
        {
            return new Mix
            {
                Id = mix.Id,
                Title = mix.Title,
                Url = mix.Url,
                Description = mix.Description,
                Intro = intro,
                Duration = mix.Duration,
                ImageUrl = mix.ImageUrl,
                Tracklist = mix.Tracklist,
                Genre = mix.Genre,
                Energy = mix.Energy,
                BpmMin = mix.BpmMin,
                BpmMax = mix.BpmMax,
                Moods = mix.Moods,
                PublishedAt = mix.PublishedAt,
            };
        }

        private static IReadOnlyList<Mix> MergeCatalogs(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            var byUrl = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);

            foreach (var mix in blobMixes)
            {
                byUrl[mix.Url] = mix;
            }

            foreach (var mix in rssMixes)
            {
                if (byUrl.TryGetValue(mix.Url, out Mix? existing))
                {
                    // When description changes and the RSS mix has a valid changsta schema block
                    // (indicated by a non-empty Genre), sync all schema fields from RSS so that
                    // edits to the SoundCloud description are reflected on the next cache flush.
                    // Without a schema block the blob metadata is preserved unchanged.
                    bool descriptionChanged = !string.Equals(
                        mix.Description, existing.Description, StringComparison.Ordinal);
                    bool rssHasSchema = !string.IsNullOrEmpty(mix.Genre);
                    bool syncSchema = descriptionChanged && rssHasSchema;

                    byUrl[mix.Url] = new Mix
                    {
                        Id = existing.Id,
                        Title = mix.Title,
                        Url = existing.Url,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? existing.Duration,
                        ImageUrl = mix.ImageUrl ?? existing.ImageUrl,
                        Tracklist = syncSchema ? mix.Tracklist : existing.Tracklist,
                        Genre = syncSchema ? mix.Genre : existing.Genre,
                        Energy = syncSchema ? mix.Energy : existing.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : existing.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : existing.BpmMax,
                        Moods = syncSchema ? mix.Moods : existing.Moods,
                        PublishedAt = mix.PublishedAt ?? existing.PublishedAt,
                    };
                }
                else
                {
                    byUrl[mix.Url] = mix;
                }
            }

            var blobUrls = new HashSet<string>(
                blobMixes.Select(m => m.Url),
                StringComparer.OrdinalIgnoreCase);

            var result = new List<Mix>(byUrl.Count);

            // New RSS discoveries (not in blob) go first — newest at front
            foreach (var mix in rssMixes)
            {
                if (!blobUrls.Contains(mix.Url) && !IsMetadataOnlyRssMix(mix))
                {
                    result.Add(mix);
                }
            }

            // Blob entries follow in their original order, updated with RSS data where applicable
            foreach (var mix in blobMixes)
            {
                result.Add(byUrl[mix.Url]);
            }

            return result;
        }

        private static bool IsMetadataOnlyRssMix(Mix mix)
        {
            return string.IsNullOrWhiteSpace(mix.Genre)
                && string.IsNullOrWhiteSpace(mix.Energy)
                && mix.Tracklist.Count == 0
                && mix.Moods.Count == 0
                && mix.BpmMin is null
                && mix.BpmMax is null;
        }

        private static int CountNewDiscoveries(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            if (rssMixes.Count == 0)
            {
                return 0;
            }

            var blobUrls = new HashSet<string>(
                blobMixes.Select(m => m.Url),
                StringComparer.OrdinalIgnoreCase);

            return rssMixes.Count(m => !blobUrls.Contains(m.Url) && !IsMetadataOnlyRssMix(m));
        }

        private static int CountUpdatedEntries(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            if (rssMixes.Count == 0)
            {
                return 0;
            }

            var blobByUrl = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < blobMixes.Count; i++)
            {
                blobByUrl[blobMixes[i].Url] = blobMixes[i];
            }

            int count = 0;

            for (int i = 0; i < rssMixes.Count; i++)
            {
                Mix rssMix = rssMixes[i];

                if (!blobByUrl.TryGetValue(rssMix.Url, out Mix? blobMix))
                {
                    continue;
                }

                if (!string.Equals(rssMix.Description, blobMix.Description, StringComparison.Ordinal)
                    || !string.Equals(rssMix.Title, blobMix.Title, StringComparison.Ordinal))
                {
                    count++;
                    continue;
                }

                string? effectiveDuration = rssMix.Duration ?? blobMix.Duration;
                string? effectiveImageUrl = rssMix.ImageUrl ?? blobMix.ImageUrl;

                if (!string.Equals(effectiveDuration, blobMix.Duration, StringComparison.Ordinal)
                    || !string.Equals(effectiveImageUrl, blobMix.ImageUrl, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private async Task<IReadOnlyList<Mix>> FetchRssSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _innerProvider
                    .GetLatestAsync(int.MaxValue, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RSS feed fetch failed — proceeding with blob catalog only.");
                return Array.Empty<Mix>();
            }
        }
    }
}
