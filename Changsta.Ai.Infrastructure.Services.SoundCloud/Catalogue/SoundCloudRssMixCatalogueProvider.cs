using System.ServiceModel.Syndication;
using System.Xml;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Models;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing;
using Microsoft.Extensions.Caching.Memory;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue
{
    public sealed class SoundCloudRssMixCatalogueProvider : IMixCatalogueProvider
    {
        private const string CacheKeyPrefix = "soundcloud_rss_";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        private readonly HttpClient _httpClient;
        private readonly string _rssUrl;
        private readonly IMemoryCache _cache;

        public SoundCloudRssMixCatalogueProvider(HttpClient httpClient, string rssUrl, IMemoryCache cache)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _rssUrl = rssUrl ?? throw new ArgumentNullException(nameof(rssUrl));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
            string cacheKey = CacheKeyPrefix + maxItems;

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Mix>? cached) && cached is not null)
            {
                return cached;
            }

            using var response = await _httpClient.GetAsync(_rssUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = XmlReader.Create(stream);

            SyndicationFeed? feed = SyndicationFeed.Load(reader);
            if (feed is null)
            {
                throw new HttpRequestException("Unable to parse RSS feed.");
            }

            if (feed.Items is null)
            {
                throw new HttpRequestException("RSS feed contains no items collection.");
            }

            IReadOnlyList<Mix> mixes = feed.Items
                .Take(maxItems)
                .Select(MapItem)
                .ToArray();

            _cache.Set(cacheKey, mixes, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl,
            });

            return mixes;
        }

        private static Mix MapItem(SyndicationItem item)
        {
            string description = item.Summary?.Text ?? string.Empty;

            string id = item.Id ?? item.Links.FirstOrDefault()?.Uri.ToString() ?? Guid.NewGuid().ToString();

            var mixSchema = MixSchemaExtractor.ExtractOrThrow(description, id);

            var mix = new Mix
            {
                Id = id,
                Title = item.Title?.Text ?? "Untitled",
                Url = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
                Description = description,
                IntroText = TracklistExtractor.ExtractIntroText(description),
                Tracklist = TracklistExtractor.Extract(description),
                PublishedAt = item.PublishDate != DateTimeOffset.MinValue ? item.PublishDate : null,

                Genre = mixSchema.Genre ?? string.Empty,
                Energy = mixSchema.Energy ?? string.Empty,
                BpmMin = mixSchema.BpmMin,
                BpmMax = mixSchema.BpmMax,
                Moods = mixSchema.Moods,
            };

            return mix;
        }
    }
}
