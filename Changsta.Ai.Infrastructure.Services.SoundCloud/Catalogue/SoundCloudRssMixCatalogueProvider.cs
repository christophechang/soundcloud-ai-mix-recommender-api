using System.ServiceModel.Syndication;
using System.Xml;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue
{
    public sealed class SoundCloudRssMixCatalogueProvider : IMixCatalogueProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _rssUrl;

        public SoundCloudRssMixCatalogueProvider(HttpClient httpClient, string rssUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _rssUrl = rssUrl ?? throw new ArgumentNullException(nameof(rssUrl));
        }

        public async Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
        {
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

            return feed.Items
                .Take(maxItems)
                .Select(MapItem)
                .ToArray();
        }

        private static Mix MapItem(SyndicationItem item)
        {
            string description = item.Summary?.Text ?? string.Empty;

            var mix = new Mix
            {
                Id = item.Id ?? item.Links.FirstOrDefault()?.Uri.ToString() ?? Guid.NewGuid().ToString(),
                Title = item.Title?.Text ?? "Untitled",
                Url = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
                Description = description,
                IntroText = TracklistExtractor.ExtractIntroText(description),
                Tracklist = TracklistExtractor.Extract(description),
                Tags = TagExtractor.Extract(description),
                PublishedAt = item.PublishDate != DateTimeOffset.MinValue ? item.PublishDate : null,
            };

            return mix;
        }
    }
}