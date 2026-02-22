using System.Net;
using System.Text;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing;
using Changsta.Ai.Infrastructure.Tests.Helpers;
using NUnit.Framework.Legacy;

namespace Changsta.Ai.Infrastructure.Tests.Services.SoundCloud.Catalogue
{
    [TestFixture]
    public sealed class SoundCloudRssMixCatalogueProviderTests
    {
        private const string RssUrl = "https://example.test/soundcloud/rss";

        [Test]
        public async Task GetLatestAsync_returns_mapped_mixes_from_rss_file()
        {
            // Arrange
            string rss = await TestDataFile.ReadAllTextAsync(@"TestData\soundcloud_rss_sample.xml");

            using var httpClient = CreateHttpClient(
                statusCode: HttpStatusCode.OK,
                body: rss,
                expectedRequestUri: RssUrl);

            var sut = new SoundCloudRssMixCatalogueProvider(httpClient, RssUrl);

            // Act
            var result = await sut.GetLatestAsync(maxItems: 50, cancellationToken: CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(41));

            var first = result[0];
            Assert.That(first.Id, Is.EqualTo("tag:soundcloud,2010:tracks/2267079023"));
            Assert.That(first.Title, Is.EqualTo("The Sunflower Mix 2"));
            Assert.That(first.Url, Is.EqualTo("https://soundcloud.com/changsta/sunflower-mix-2"));
            Assert.That(first.PublishedAt, Is.Not.Null);

            // The provider maps these from RSS description via your extractors
            Assert.That(first.Description, Does.Contain("The Sunflower Mix 2 is all warm"));
            Assert.That(first.IntroText, Is.EqualTo(TracklistExtractor.ExtractIntroText(first.Description)));
            CollectionAssert.AreEqual(TracklistExtractor.Extract(first.Description), first.Tracklist);
            CollectionAssert.AreEqual(TagExtractor.Extract(first.Description), first.Tags);

            var second = result[1];
            Assert.That(second.Id, Is.EqualTo("tag:soundcloud,2010:tracks/2266445624"));
            Assert.That(second.Title, Is.EqualTo("Subsonic Sessions 4"));
            Assert.That(second.Url, Is.EqualTo("https://soundcloud.com/changsta/subsonic-sessions-4"));
            Assert.That(second.PublishedAt, Is.Not.Null);

            Assert.That(second.Description, Does.Contain("Subsonic Sessions 4 moves through deep"));
            Assert.That(second.IntroText, Is.EqualTo(TracklistExtractor.ExtractIntroText(second.Description)));
            CollectionAssert.AreEqual(TracklistExtractor.Extract(second.Description), second.Tracklist);
            CollectionAssert.AreEqual(TagExtractor.Extract(second.Description), second.Tags);
        }

        [Test]
        public async Task GetLatestAsync_respects_maxItems()
        {
            // Arrange
            string rss = await TestDataFile.ReadAllTextAsync(@"TestData\soundcloud_rss_sample.xml");

            using var httpClient = CreateHttpClient(
                statusCode: HttpStatusCode.OK,
                body: rss,
                expectedRequestUri: RssUrl);

            var sut = new SoundCloudRssMixCatalogueProvider(httpClient, RssUrl);

            // Act
            var result = await sut.GetLatestAsync(maxItems: 1, cancellationToken: CancellationToken.None);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
        }

        [Test]
        public void GetLatestAsync_throws_when_http_status_is_not_success()
        {
            // Arrange
            using var httpClient = CreateHttpClient(
                statusCode: HttpStatusCode.InternalServerError,
                body: "oops",
                expectedRequestUri: RssUrl);

            var sut = new SoundCloudRssMixCatalogueProvider(httpClient, RssUrl);

            // Act + Assert
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await sut.GetLatestAsync(maxItems: 10, cancellationToken: CancellationToken.None));
        }

        private static HttpClient CreateHttpClient(HttpStatusCode statusCode, string body, string expectedRequestUri)
        {
            var handler = new StubHttpMessageHandler((request, ct) =>
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                Assert.That(request.RequestUri, Is.Not.Null);
                Assert.That(request.RequestUri!.ToString(), Is.EqualTo(expectedRequestUri));

                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/rss+xml"),
                };

                return Task.FromResult(response);
            });

            return new HttpClient(handler, disposeHandler: true);
        }
    }
}