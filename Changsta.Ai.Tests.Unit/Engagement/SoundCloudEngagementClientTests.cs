using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement;
using Changsta.Ai.Tests.Unit.Helpers;
using Changsta.Ai.Tests.Unit.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Engagement
{
    [TestFixture]
    public sealed class SoundCloudEngagementClientTests
    {
        private const long UserId = 123456;
        private const string Token = "fixture-access-token";
        private const string FirstPageUrl = "https://api.soundcloud.com/users/123456/tracks?limit=50&linked_partitioning=true";
        private const string SecondPageUrl = "https://api.soundcloud.com/users/123456/tracks?limit=50&linked_partitioning=true&cursor=fixture-cursor-2";

        [Test]
        public async Task FetchAllAsync_maps_counts_from_single_page()
        {
            using var harness = new Harness(url => url == FirstPageUrl
                ? Json(HttpStatusCode.OK, "{\"collection\":[{\"id\":2432119239,\"playback_count\":1204,\"favoritings_count\":87,\"likes_count\":5}]}")
                : NotFound());

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Should().ContainSingle();
            result[0].TrackId.Should().Be(2432119239);
            result[0].Plays.Should().Be(1204);
            result[0].Likes.Should().Be(87);
        }

        [Test]
        public async Task FetchAllAsync_follows_next_href_until_absent()
        {
            using var harness = await Harness.FromFixturesAsync();

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Select(e => e.TrackId).Should().Equal(1000000001, 1000000002, 1000000003);
            harness.ApiRequests.Should().Equal(FirstPageUrl, SecondPageUrl);
        }

        [Test]
        public async Task FetchAllAsync_sends_oauth_token_header()
        {
            using var harness = await Harness.FromFixturesAsync();

            await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            harness.ApiAuthorizations.Should().HaveCount(2);
            harness.ApiAuthorizations.Should().OnlyContain(a => a == $"OAuth {Token}");
        }

        [Test]
        public async Task FetchAllAsync_maps_absent_counts_to_null()
        {
            using var harness = await Harness.FromFixturesAsync();

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            MixEngagement bare = result!.Single(e => e.TrackId == 1000000003);
            bare.Plays.Should().BeNull();
            bare.Likes.Should().BeNull();

            MixEngagement full = result!.Single(e => e.TrackId == 1000000001);
            full.Plays.Should().Be(1204);
            full.Likes.Should().Be(87);
        }

        [Test]
        public async Task FetchAllAsync_accepts_an_explicitly_empty_collection()
        {
            using var harness = new Harness(_ => Json(HttpStatusCode.OK, "{\"collection\":[]}"));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Should().BeEmpty();
        }

        [Test]
        public async Task FetchAllAsync_returns_null_when_collection_is_missing()
        {
            string page1 = await ReadFixtureAsync("soundcloud_user_tracks_page1.json");
            using var harness = new Harness(url => url == FirstPageUrl
                ? Json(HttpStatusCode.OK, page1)
                : Json(HttpStatusCode.OK, "{}"));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
            harness.ApiRequests.Should().HaveCount(2);
        }

        [TestCase("{\"collection\":[{\"playback_count\":5}]}")]
        [TestCase("{\"collection\":[{\"id\":0,\"playback_count\":5}]}")]
        [TestCase("{\"collection\":[{\"id\":-7,\"playback_count\":5}]}")]
        [TestCase("{\"collection\":[{\"id\":42},null]}")]
        public async Task FetchAllAsync_returns_null_when_a_track_has_no_positive_id(string body)
        {
            using var harness = new Harness(_ => Json(HttpStatusCode.OK, body));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
        }

        [Test]
        public async Task FetchAllAsync_returns_null_when_any_page_fails()
        {
            string page1 = await ReadFixtureAsync("soundcloud_user_tracks_page1.json");
            using var harness = new Harness(url => url == FirstPageUrl
                ? Json(HttpStatusCode.OK, page1)
                : Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
            harness.ApiRequests.Should().Equal(FirstPageUrl, SecondPageUrl);
        }

        [Test]
        public async Task FetchAllAsync_invalidates_token_on_401()
        {
            using var harness = new Harness(_ => Json(HttpStatusCode.Unauthorized, "{}"));

            IReadOnlyList<MixEngagement>? first = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);
            first.Should().BeNull();
            harness.TokenRequests.Should().Be(1);

            await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            harness.TokenRequests.Should().Be(2, "the rejected token must not be reused");
        }

        [Test]
        public async Task FetchAllAsync_keeps_token_cached_on_other_failures()
        {
            using var harness = new Harness(_ => Json(HttpStatusCode.InternalServerError, "{}"));

            await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);
            await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            harness.TokenRequests.Should().Be(1);
        }

        [Test]
        public async Task FetchAllAsync_returns_null_on_token_failure()
        {
            using var harness = new Harness(
                _ => Json(HttpStatusCode.OK, "{\"collection\":[]}"),
                tokenStatus: HttpStatusCode.BadRequest);

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
            harness.ApiRequests.Should().BeEmpty();
        }

        [TestCase("not json at all")]
        [TestCase("{\"collection\":[{\"id\":\"abc\"}]}")]
        [TestCase("{\"collection\":")]
        public async Task FetchAllAsync_returns_null_on_malformed_json(string body)
        {
            using var harness = new Harness(_ => Json(HttpStatusCode.OK, body));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
        }

        [Test]
        public async Task FetchAllAsync_returns_null_on_http_client_timeout()
        {
            using var harness = new Harness(_ => throw new TaskCanceledException("The request timed out."));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
        }

        [TestCase("https://evil.example/x")]
        [TestCase("http://api.soundcloud.com/x")]
        [TestCase("https://api.soundcloud.com.evil.example/x")]
        [TestCase("https://api.soundcloud.com:8443/x")]
        [TestCase("/users/123456/tracks?cursor=relative")]
        public async Task FetchAllAsync_refuses_next_href_on_another_host(string nextHref)
        {
            using var harness = new Harness(url => url == FirstPageUrl
                ? Json(HttpStatusCode.OK, $"{{\"collection\":[{{\"id\":1}}],\"next_href\":\"{nextHref}\"}}")
                : Json(HttpStatusCode.OK, "{\"collection\":[]}"));

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
            harness.ApiRequests.Should().Equal(FirstPageUrl);
        }

        [Test]
        public async Task FetchAllAsync_stops_at_page_cap()
        {
            int page = 0;
            using var harness = new Harness(_ =>
            {
                int n = Interlocked.Increment(ref page);
                return Json(
                    HttpStatusCode.OK,
                    $"{{\"collection\":[{{\"id\":{n}}}],\"next_href\":\"https://api.soundcloud.com/users/123456/tracks?cursor={n}\"}}");
            });

            IReadOnlyList<MixEngagement>? result = await harness.Sut.FetchAllAsync(UserId, CancellationToken.None);

            result.Should().BeNull();
            harness.ApiRequests.Should().HaveCount(20);
        }

        [Test]
        public async Task FetchAllAsync_propagates_cancellation_of_its_token()
        {
            using var harness = await Harness.FromFixturesAsync();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            Func<Task> act = () => harness.Sut.FetchAllAsync(UserId, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task FetchAllAsync_propagates_cancellation_raised_mid_request()
        {
            using var cts = new CancellationTokenSource();
            using var harness = new Harness(_ =>
            {
                cts.Cancel();
                throw new TaskCanceledException("cancelled", null, cts.Token);
            });

            Func<Task> act = () => harness.Sut.FetchAllAsync(UserId, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        private static Task<string> ReadFixtureAsync(string name) =>
            TestDataFile.ReadAllTextAsync(Path.Combine("TestData", name));

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

        private static HttpResponseMessage NotFound() => Json(HttpStatusCode.NotFound, "{}");

        private sealed class Harness : IDisposable
        {
            private readonly HttpClient _tokenHttp;
            private readonly HttpClient _apiHttp;
            private readonly SoundCloudAppTokenCache _tokenCache;
            private readonly ConcurrentQueue<string> _apiRequests = new();
            private readonly ConcurrentQueue<string?> _apiAuthorizations = new();
            private int _tokenRequests;

            public Harness(Func<string, HttpResponseMessage> api, HttpStatusCode tokenStatus = HttpStatusCode.OK)
            {
                _tokenHttp = new HttpClient(new StubHttpMessageHandler((request, ct) =>
                {
                    Interlocked.Increment(ref _tokenRequests);
                    return Task.FromResult(tokenStatus == HttpStatusCode.OK
                        ? Json(HttpStatusCode.OK, $"{{\"access_token\":\"{Token}\",\"expires_in\":3600}}")
                        : Json(tokenStatus, "{}"));
                }));

                _apiHttp = new HttpClient(new StubHttpMessageHandler((request, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    string url = request.RequestUri!.ToString();
                    _apiRequests.Enqueue(url);
                    _apiAuthorizations.Enqueue(request.Headers.Authorization?.ToString());
                    return Task.FromResult(api(url));
                }));

                _tokenCache = new SoundCloudAppTokenCache(
                    _tokenHttp,
                    new SoundCloudEngagementSettings { ClientId = "id", ClientSecret = "secret" },
                    new FakeTimeProvider(),
                    NullLogger<SoundCloudAppTokenCache>.Instance);

                Sut = new SoundCloudEngagementClient(_apiHttp, _tokenCache, NullLogger<SoundCloudEngagementClient>.Instance);
            }

            public SoundCloudEngagementClient Sut { get; }

            public IReadOnlyList<string> ApiRequests => _apiRequests.ToArray();

            public IReadOnlyList<string?> ApiAuthorizations => _apiAuthorizations.ToArray();

            public int TokenRequests => Volatile.Read(ref _tokenRequests);

            public static async Task<Harness> FromFixturesAsync()
            {
                string page1 = await ReadFixtureAsync("soundcloud_user_tracks_page1.json");
                string page2 = await ReadFixtureAsync("soundcloud_user_tracks_page2.json");

                return new Harness(url => url switch
                {
                    FirstPageUrl => Json(HttpStatusCode.OK, page1),
                    SecondPageUrl => Json(HttpStatusCode.OK, page2),
                    _ => NotFound(),
                });
            }

            public void Dispose()
            {
                _tokenCache.Dispose();
                _tokenHttp.Dispose();
                _apiHttp.Dispose();
            }
        }
    }
}
