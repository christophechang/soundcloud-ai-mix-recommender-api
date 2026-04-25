using System.ServiceModel.Syndication;
using System.Xml;
using System.Xml.Linq;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Models;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue
{
    public sealed class SoundCloudRssMixCatalogueProvider : IMixCatalogueProvider
    {
        private const string CacheKeyPrefix = "soundcloud_rss_v";
        private const string ItunesNamespace = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        private readonly HttpClient _httpClient;
        private readonly string _rssUrl;
        private readonly IMemoryCache _cache;
        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly ILogger<SoundCloudRssMixCatalogueProvider> _logger;

        public SoundCloudRssMixCatalogueProvider(HttpClient httpClient, string rssUrl, IMemoryCache cache, ICatalogCacheInvalidator invalidator, ILogger<SoundCloudRssMixCatalogueProvider> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _rssUrl = rssUrl ?? throw new ArgumentNullException(nameof(rssUrl));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
            string cacheKey = $"{CacheKeyPrefix}{_invalidator.Version}_{maxItems}";

            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Mix>? cached) && cached is not null)
            {
                return cached;
            }

            using var response = await _httpClient.GetAsync(_rssUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using var reader = XmlReader.Create(stream, xmlSettings);

            SyndicationFeed? feed = SyndicationFeed.Load(reader);
            if (feed is null)
            {
                throw new HttpRequestException("Unable to parse RSS feed.");
            }

            if (feed.Items is null)
            {
                throw new HttpRequestException("RSS feed contains no items collection.");
            }

            var mixList = new List<Mix>();

            foreach (var item in feed.Items.Take(maxItems))
            {
                try
                {
                    mixList.Add(MapItem(item));
                }
                catch (Exception ex)
                {
                    // Skip items whose schema cannot be parsed rather than failing the whole feed.
                    _logger.LogWarning(ex, "Skipping RSS item '{ItemId}' — failed to parse.", item.Id);
                    continue;
                }
            }

            IReadOnlyList<Mix> mixes = mixList;

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

            MixSchema? mixSchema;
            try
            {
                mixSchema = MixSchemaExtractor.ExtractOrThrow(description, id);
            }
            catch (InvalidOperationException)
            {
                mixSchema = null;
            }

            var mix = new Mix
            {
                Id = id,
                Title = item.Title?.Text ?? "Untitled",
                Url = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
                Description = description,
                Duration = GetItunesElementValue(item, "duration"),
                ImageUrl = GetItunesImageUrl(item),
                Tracklist = mixSchema is null ? Array.Empty<Track>() : TracklistExtractor.Extract(description),

                Genre = mixSchema?.Genre ?? string.Empty,
                Energy = mixSchema?.Energy ?? string.Empty,
                BpmMin = mixSchema?.BpmMin,
                BpmMax = mixSchema?.BpmMax,
                Moods = mixSchema?.Moods ?? Array.Empty<string>(),
                PublishedAt = item.PublishDate == default ? null : item.PublishDate,
            };

            return mix;
        }

        private static string? GetItunesElementValue(SyndicationItem item, string elementName)
        {
            string? value = item.ElementExtensions
                .ReadElementExtensions<string>(elementName, ItunesNamespace)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? GetItunesImageUrl(SyndicationItem item)
        {
            SyndicationElementExtension? extension = item.ElementExtensions
                .FirstOrDefault(e => string.Equals(e.OuterName, "image", StringComparison.Ordinal)
                    && string.Equals(e.OuterNamespace, ItunesNamespace, StringComparison.Ordinal));

            if (extension is null)
            {
                return null;
            }

            XElement element = extension.GetObject<XElement>();
            string? href = element.Attribute("href")?.Value;

            return string.IsNullOrWhiteSpace(href) ? null : href.Trim();
        }
    }
}
