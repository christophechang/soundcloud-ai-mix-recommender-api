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

                bool relatedMixesChanged;
                merged = RelatedMixScorer.ComputeRelatedMixes(merged, out relatedMixesChanged);

                int newDiscoveries = CountNewDiscoveries(blobMixes, rssMixes);
                int updatedEntries = CountUpdatedEntries(blobMixes, rssMixes);

                if (blobReadSucceeded && (newDiscoveries > 0 || updatedEntries > 0 || blobGenresChanged || introHydrationChanged || relatedMixesChanged))
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
                RelatedMixes = mix.RelatedMixes,
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
                RelatedMixes = mix.RelatedMixes,
                PublishedAt = mix.PublishedAt,
            };
        }

        private static IReadOnlyList<Mix> MergeCatalogs(
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes)
        {
            var byUrl = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, Mix>(StringComparer.OrdinalIgnoreCase);

            foreach (var mix in blobMixes)
            {
                byUrl[mix.Url] = mix;
                if (!string.IsNullOrEmpty(mix.Id))
                {
                    byId[mix.Id] = mix;
                }
            }

            // Maps old blob URL → new RSS URL when a mix's SoundCloud permalink changes.
            var movedOldToNew = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                        Id = ResolveStableId(existing.Id, mix.Id),
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
                        RelatedMixes = existing.RelatedMixes,
                        PublishedAt = mix.PublishedAt ?? existing.PublishedAt,
                    };
                }
                else if (!string.IsNullOrEmpty(mix.Id)
                    && byId.TryGetValue(mix.Id, out Mix? priorEntry))
                {
                    // URL changed: same SoundCloud track ID, new permalink — transfer computed
                    // data to the new URL and retire the old one.
                    movedOldToNew[priorEntry.Url] = mix.Url;

                    bool descriptionChanged = !string.Equals(
                        mix.Description, priorEntry.Description, StringComparison.Ordinal);
                    bool rssHasSchema = !string.IsNullOrEmpty(mix.Genre);
                    bool syncSchema = descriptionChanged && rssHasSchema;

                    byUrl[mix.Url] = new Mix
                    {
                        Id = ResolveStableId(priorEntry.Id, mix.Id),
                        Title = mix.Title,
                        Url = mix.Url,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? priorEntry.Duration,
                        ImageUrl = mix.ImageUrl ?? priorEntry.ImageUrl,
                        Tracklist = syncSchema ? mix.Tracklist : priorEntry.Tracklist,
                        Genre = syncSchema ? mix.Genre : priorEntry.Genre,
                        Energy = syncSchema ? mix.Energy : priorEntry.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : priorEntry.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : priorEntry.BpmMax,
                        Moods = syncSchema ? mix.Moods : priorEntry.Moods,
                        RelatedMixes = priorEntry.RelatedMixes,
                        PublishedAt = mix.PublishedAt ?? priorEntry.PublishedAt,
                    };
                }
                else if (TryFindLegacyMovedEntry(mix, blobMixes, out Mix? legacyEntry))
                {
                    // Earlier catalog rows used the SoundCloud URL as Id. If a permalink
                    // changes before that row has been hydrated with the stable RSS GUID,
                    // use immutable metadata and tracklist evidence to migrate it once.
                    movedOldToNew[legacyEntry.Url] = mix.Url;

                    bool descriptionChanged = !string.Equals(
                        mix.Description, legacyEntry.Description, StringComparison.Ordinal);
                    bool rssHasSchema = !string.IsNullOrEmpty(mix.Genre);
                    bool syncSchema = descriptionChanged && rssHasSchema;

                    byUrl[mix.Url] = new Mix
                    {
                        Id = ResolveStableId(legacyEntry.Id, mix.Id),
                        Title = mix.Title,
                        Url = mix.Url,
                        Description = mix.Description,
                        Intro = mix.Intro,
                        Duration = mix.Duration ?? legacyEntry.Duration,
                        ImageUrl = mix.ImageUrl ?? legacyEntry.ImageUrl,
                        Tracklist = syncSchema ? mix.Tracklist : legacyEntry.Tracklist,
                        Genre = syncSchema ? mix.Genre : legacyEntry.Genre,
                        Energy = syncSchema ? mix.Energy : legacyEntry.Energy,
                        BpmMin = syncSchema ? mix.BpmMin : legacyEntry.BpmMin,
                        BpmMax = syncSchema ? mix.BpmMax : legacyEntry.BpmMax,
                        Moods = syncSchema ? mix.Moods : legacyEntry.Moods,
                        RelatedMixes = legacyEntry.RelatedMixes,
                        PublishedAt = mix.PublishedAt ?? legacyEntry.PublishedAt,
                    };
                }
                else
                {
                    byUrl[mix.Url] = mix;
                }
            }

            var blobUrlSet = new HashSet<string>(
                blobMixes.Select(m => m.Url),
                StringComparer.OrdinalIgnoreCase);

            var movedNewUrls = new HashSet<string>(movedOldToNew.Values, StringComparer.OrdinalIgnoreCase);

            var result = new List<Mix>(byUrl.Count);

            // New RSS discoveries (not in blob, not a URL-moved entry) go first — newest at front
            foreach (var mix in rssMixes)
            {
                if (!blobUrlSet.Contains(mix.Url) && !movedNewUrls.Contains(mix.Url) && !IsMetadataOnlyRssMix(mix))
                {
                    result.Add(mix);
                }
            }

            // Blob entries follow in their original order; moved entries use the new URL, orphaned old URLs are dropped
            foreach (var mix in blobMixes)
            {
                if (movedOldToNew.TryGetValue(mix.Url, out string? newUrl))
                {
                    result.Add(byUrl[newUrl]);
                }
                else
                {
                    result.Add(byUrl[mix.Url]);
                }
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

        private static bool TryFindLegacyMovedEntry(
            Mix rssMix,
            IReadOnlyList<Mix> blobMixes,
            out Mix legacyEntry)
        {
            for (int i = 0; i < blobMixes.Count; i++)
            {
                Mix candidate = blobMixes[i];

                if (!IsUrlLikeId(candidate.Id)
                    || string.Equals(candidate.Url, rssMix.Url, StringComparison.OrdinalIgnoreCase)
                    || !SamePublishedAt(candidate, rssMix)
                    || !SameTracklist(candidate.Tracklist, rssMix.Tracklist))
                {
                    continue;
                }

                legacyEntry = candidate;
                return true;
            }

            legacyEntry = null!;
            return false;
        }

        private static bool SamePublishedAt(Mix a, Mix b)
        {
            if (a.PublishedAt is null || b.PublishedAt is null)
            {
                return false;
            }

            return a.PublishedAt.Value.Equals(b.PublishedAt.Value);
        }

        private static bool SameTracklist(IReadOnlyList<Track> a, IReadOnlyList<Track> b)
        {
            if (a.Count == 0 || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].Artist, b[i].Artist, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(a[i].Title, b[i].Title, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ResolveStableId(string existingId, string rssId)
        {
            if (IsUrlLikeId(existingId) && !string.IsNullOrWhiteSpace(rssId) && !IsUrlLikeId(rssId))
            {
                return rssId;
            }

            return existingId;
        }

        private static bool IsUrlLikeId(string id)
        {
            return id.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
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
                string effectiveId = ResolveStableId(blobMix.Id, rssMix.Id);

                if (!string.Equals(effectiveDuration, blobMix.Duration, StringComparison.Ordinal)
                    || !string.Equals(effectiveImageUrl, blobMix.ImageUrl, StringComparison.Ordinal)
                    || !string.Equals(effectiveId, blobMix.Id, StringComparison.Ordinal))
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
