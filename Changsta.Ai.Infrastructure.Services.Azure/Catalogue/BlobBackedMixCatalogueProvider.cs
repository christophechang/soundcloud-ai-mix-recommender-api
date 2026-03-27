using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    public sealed class BlobBackedMixCatalogueProvider : IMixCatalogueProvider
    {
        private const string CacheKey = "blob_catalog_";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        private readonly IMixCatalogueProvider _innerProvider;
        private readonly IBlobMixCatalogueRepository _repository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<BlobBackedMixCatalogueProvider> _logger;

        public BlobBackedMixCatalogueProvider(
            IMixCatalogueProvider innerProvider,
            IBlobMixCatalogueRepository repository,
            IMemoryCache cache,
            ILogger<BlobBackedMixCatalogueProvider> logger)
        {
            _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(CacheKey, out IReadOnlyList<Mix>? cached) && cached is not null)
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

            IReadOnlyList<Mix> rssMixes = await FetchRssSafeAsync(cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<Mix> merged = MergeCatalogs(blobMixes, rssMixes);

            int newDiscoveries = CountNewDiscoveries(blobMixes, rssMixes);
            int updatedEntries = CountUpdatedEntries(blobMixes, rssMixes);

            if (blobReadSucceeded && (newDiscoveries > 0 || updatedEntries > 0))
            {
                _logger.LogInformation(
                    "Writing blob catalog — {NewCount} new mixes, {UpdateCount} updated entries.",
                    newDiscoveries,
                    updatedEntries);

                await _repository.WriteAsync(merged, cancellationToken).ConfigureAwait(false);
            }

            _cache.Set(CacheKey, merged, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
            });

            return merged.Count > maxItems ? merged.Take(maxItems).ToArray() : merged;
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
                    // Preserve curated blob metadata (genre, energy, BPM, moods, tracklist).
                    // Only update title and description, which reflect live RSS changes.
                    byUrl[mix.Url] = new Mix
                    {
                        Id = existing.Id,
                        Title = mix.Title,
                        Url = existing.Url,
                        Description = mix.Description,
                        Tracklist = existing.Tracklist,
                        Genre = existing.Genre,
                        Energy = existing.Energy,
                        BpmMin = existing.BpmMin,
                        BpmMax = existing.BpmMax,
                        Moods = existing.Moods,
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
                if (!blobUrls.Contains(mix.Url))
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

            return rssMixes.Count(m => !blobUrls.Contains(m.Url));
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
