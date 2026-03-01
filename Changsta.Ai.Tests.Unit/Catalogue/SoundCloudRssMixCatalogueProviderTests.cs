using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Catalogue;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing;
using Changsta.Ai.Infrastructure.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

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
            string rss = await TestDataFile.ReadAllTextAsync(
                Path.Combine("TestData", "soundcloud_rss_sample.xml"));

            using var httpClient = CreateHttpClient(
                statusCode: HttpStatusCode.OK,
                body: rss,
                expectedRequestUri: RssUrl);

            var sut = new SoundCloudRssMixCatalogueProvider(httpClient, RssUrl, new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await sut.GetLatestAsync(
                maxItems: 50,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(42));

            // First
            var first = result[0];
            Assert.That(first.Id, Is.EqualTo("tag:soundcloud,2010:tracks/2272564208"));
            Assert.That(first.Title, Is.EqualTo("Murda Classics D&B Mix"));
            Assert.That(first.Url, Is.EqualTo("https://soundcloud.com/changsta/murda-classics-d-b-mix"));
            Assert.That(first.Tracklist, Is.EqualTo(TracklistExtractor.Extract(first.Description)));

            AssertMixSchemaMapped(first);
        }

        [Test]
        public async Task GetLatestAsync_respects_maxItems()
        {
            // Arrange
            string rss = await TestDataFile.ReadAllTextAsync(
                Path.Combine("TestData", "soundcloud_rss_sample.xml"));

            using var httpClient = CreateHttpClient(
                statusCode: HttpStatusCode.OK,
                body: rss,
                expectedRequestUri: RssUrl);

            var sut = new SoundCloudRssMixCatalogueProvider(httpClient, RssUrl, new MemoryCache(new MemoryCacheOptions()));

            // Act
            var result = await sut.GetLatestAsync(
                maxItems: 1,
                cancellationToken: CancellationToken.None);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            AssertMixSchemaMapped(result[0]);
        }

        [Test]
        public void GetLatestAsync_throws_when_http_status_is_not_success()
        {
            // Arrange
            using var httpClient = CreateHttpClient(
                statusCode: HttpStatusCode.InternalServerError,
                body: "oops",
                expectedRequestUri: RssUrl);

            var sut = new SoundCloudRssMixCatalogueProvider(httpClient, RssUrl, new MemoryCache(new MemoryCacheOptions()));

            // Act + Assert
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await sut.GetLatestAsync(
                    maxItems: 10,
                    cancellationToken: CancellationToken.None));
        }

        private static void AssertMixSchemaMapped(Changsta.Ai.Core.Domain.Mix mix)
        {
            // Provider now sets required schema fields, validate they match the embedded schema in Description
            Assert.That(mix.Genre, Is.Not.Null.And.Not.Empty);
            Assert.That(mix.Energy, Is.Not.Null.And.Not.Empty);
            Assert.That(mix.Moods, Is.Not.Null);

            var schema = TryParseSchemaFromDescription(mix.Description);

            // If the sample data has no schema block, we still want required fields present
            if (schema is null)
            {
                return;
            }

            Assert.That(mix.Genre, Is.EqualTo(schema.genre));
            Assert.That(mix.Energy, Is.EqualTo(schema.energy));

            if (schema.bpmMin is null && schema.bpmMax is null)
            {
                Assert.That(mix.BpmMin, Is.Null);
                Assert.That(mix.BpmMax, Is.Null);
            }
            else
            {
                Assert.That(mix.BpmMin, Is.EqualTo(schema.bpmMin));
                Assert.That(mix.BpmMax, Is.EqualTo(schema.bpmMax));
            }

            Assert.That(mix.Moods, Is.EqualTo(schema.moods));
        }

        private static ParsedSchema? TryParseSchemaFromDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            // Looks for: [changsta:mix:v1 { ...json... }]
            const string Marker = "[changsta:mix:v1";
            int start = description.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            int end = description.IndexOf(']', start);
            if (end < 0)
            {
                return null;
            }

            string block = description.Substring(start, end - start + 1);

            int jsonStart = block.IndexOf('{');
            int jsonEnd = block.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return null;
            }

            string json = block.Substring(jsonStart, jsonEnd - jsonStart + 1);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string genre = root.TryGetProperty("genre", out var g) ? (g.GetString() ?? string.Empty) : string.Empty;
            string energy = root.TryGetProperty("energy", out var e) ? (e.GetString() ?? string.Empty) : string.Empty;

            int? bpmMin = null;
            int? bpmMax = null;
            if (root.TryGetProperty("bpm", out var bpmEl) && bpmEl.ValueKind == JsonValueKind.Array)
            {
                var ints = bpmEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Number)
                    .Select(x => x.GetInt32())
                    .ToArray();

                if (ints.Length == 1)
                {
                    bpmMin = ints[0];
                    bpmMax = ints[0];
                }
                else if (ints.Length >= 2)
                {
                    bpmMin = ints[0];
                    bpmMax = ints[1];
                }
            }

            var moods = Array.Empty<string>();
            if (root.TryGetProperty("moods", out var moodsEl) && moodsEl.ValueKind == JsonValueKind.Array)
            {
                moods = moodsEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => (x.GetString() ?? string.Empty).Trim())
                    .Where(x => x.Length > 0)
                    .ToArray();
            }

            if (string.IsNullOrWhiteSpace(genre) || string.IsNullOrWhiteSpace(energy))
            {
                return null;
            }

            return new ParsedSchema(
                genre: genre,
                energy: energy,
                bpmMin: bpmMin,
                bpmMax: bpmMax,
                moods: moods);
        }

        private sealed record ParsedSchema(
            string genre,
            string energy,
            int? bpmMin,
            int? bpmMax,
            IReadOnlyList<string> moods);

        private static HttpClient CreateHttpClient(
            HttpStatusCode statusCode,
            string body,
            string expectedRequestUri)
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