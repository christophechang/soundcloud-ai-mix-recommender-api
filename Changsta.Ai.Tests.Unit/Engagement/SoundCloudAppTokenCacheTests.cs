using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement;
using Changsta.Ai.Tests.Unit.Helpers;
using Changsta.Ai.Tests.Unit.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Engagement
{
    [TestFixture]
    public sealed class SoundCloudAppTokenCacheTests
    {
        private const string TokenEndpoint = "https://secure.soundcloud.com/oauth/token";
        private const string ClientId = "test-client-id";
        private const string ClientSecret = "test-client-secret-value";

        [Test]
        public async Task GetTokenAsync_posts_client_credentials_with_basic_auth()
        {
            var requests = new List<CapturedRequest>();
            using var http = new HttpClient(new StubHttpMessageHandler(async (request, ct) =>
            {
                requests.Add(await CapturedRequest.FromAsync(request).ConfigureAwait(false));
                return TokenResponse("tok-1", 3600);
            }));
            using var sut = CreateSut(http, new FakeTimeProvider());

            string token = await sut.GetTokenAsync(CancellationToken.None);

            token.Should().Be("tok-1");
            requests.Should().ContainSingle();
            CapturedRequest sent = requests[0];
            sent.Method.Should().Be(HttpMethod.Post);
            sent.Uri.Should().Be(TokenEndpoint);
            sent.Body.Should().Be("grant_type=client_credentials");
            string expectedBasic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}"));
            sent.Authorization.Should().Be($"Basic {expectedBasic}");
        }

        [Test]
        public async Task GetTokenAsync_reuses_token_until_expiry()
        {
            int calls = 0;
            using var http = CountingClient(() => TokenResponse($"tok-{++calls}", 3600));
            using var sut = CreateSut(http, new FakeTimeProvider());

            string first = await sut.GetTokenAsync(CancellationToken.None);
            string second = await sut.GetTokenAsync(CancellationToken.None);

            calls.Should().Be(1);
            second.Should().Be(first);
        }

        [Test]
        public async Task GetTokenAsync_refetches_after_expiry_minus_skew()
        {
            int calls = 0;
            var clock = new FakeTimeProvider();
            using var http = CountingClient(() => TokenResponse($"tok-{++calls}", 3600));
            using var sut = CreateSut(http, clock);

            await sut.GetTokenAsync(CancellationToken.None);
            clock.UtcNow = clock.UtcNow.AddSeconds(3539);
            await sut.GetTokenAsync(CancellationToken.None);
            calls.Should().Be(1, "the token is still inside expires_in minus the 60s skew");

            clock.UtcNow = clock.UtcNow.AddSeconds(2);
            string refreshed = await sut.GetTokenAsync(CancellationToken.None);

            calls.Should().Be(2);
            refreshed.Should().Be("tok-2");
        }

        [TestCase("{\"access_token\":\"tok\",\"expires_in\":30}")]
        [TestCase("{\"access_token\":\"tok\",\"expires_in\":60}")]
        [TestCase("{\"access_token\":\"tok\",\"expires_in\":0}")]
        [TestCase("{\"access_token\":\"tok\"}")]
        public async Task GetTokenAsync_does_not_cache_short_zero_or_missing_expiry(string body)
        {
            int calls = 0;
            using var http = CountingClient(() =>
            {
                calls++;
                return JsonResponse(HttpStatusCode.OK, body);
            });
            using var sut = CreateSut(http, new FakeTimeProvider());

            string first = await sut.GetTokenAsync(CancellationToken.None);
            string second = await sut.GetTokenAsync(CancellationToken.None);

            first.Should().Be("tok");
            second.Should().Be("tok");
            calls.Should().Be(2);
        }

        [Test]
        public async Task GetTokenAsync_concurrent_callers_share_one_request()
        {
            int calls = 0;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new StubHttpMessageHandler(async (request, ct) =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task.ConfigureAwait(false);
                return TokenResponse("shared", 3600);
            }));
            using var sut = CreateSut(http, new FakeTimeProvider());

            Task<string>[] callers = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => sut.GetTokenAsync(CancellationToken.None)))
                .ToArray();

            gate.SetResult();
            string[] tokens = await Task.WhenAll(callers);

            calls.Should().Be(1);
            tokens.Should().OnlyContain(t => t == "shared");
        }

        [Test]
        public async Task GetTokenAsync_cancelled_waiter_does_not_block_later_callers()
        {
            int calls = 0;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new StubHttpMessageHandler(async (request, ct) =>
            {
                Interlocked.Increment(ref calls);
                firstRequestStarted.TrySetResult();
                await gate.Task.ConfigureAwait(false);
                return TokenResponse("tok", 3600);
            }));
            using var sut = CreateSut(http, new FakeTimeProvider());

            Task<string> holder = sut.GetTokenAsync(CancellationToken.None);
            await firstRequestStarted.Task;

            using var cts = new CancellationTokenSource();
            Task<string> waiter = sut.GetTokenAsync(cts.Token);
            await cts.CancelAsync();

            Func<Task> awaitWaiter = () => waiter;
            await awaitWaiter.Should().ThrowAsync<OperationCanceledException>();

            gate.SetResult();
            (await holder).Should().Be("tok");

            string later = await sut.GetTokenAsync(CancellationToken.None);
            later.Should().Be("tok");
            calls.Should().Be(1);
        }

        [Test]
        public async Task GetTokenAsync_throws_on_non_success()
        {
            int calls = 0;
            using var http = CountingClient(() =>
            {
                calls++;
                return calls == 1
                    ? JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}")
                    : TokenResponse("tok", 3600);
            });
            using var sut = CreateSut(http, new FakeTimeProvider());

            Func<Task> act = () => sut.GetTokenAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<HttpRequestException>();
            thrown.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            thrown.Which.Message.Should().NotContain(ClientSecret).And.NotContain("invalid_client");

            await sut.GetTokenAsync(CancellationToken.None);
            calls.Should().Be(2, "a failed token request must not leave anything cached");
        }

        [Test]
        public async Task GetTokenAsync_throws_when_access_token_missing()
        {
            using var http = CountingClient(() => JsonResponse(HttpStatusCode.OK, "{}"));
            using var sut = CreateSut(http, new FakeTimeProvider());

            Func<Task> act = () => sut.GetTokenAsync(CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
            thrown.Which.Message.Should().NotContain(ClientSecret).And.NotContain(ClientId);
        }

        [Test]
        public async Task Invalidate_forces_a_new_request()
        {
            int calls = 0;
            using var http = CountingClient(() => TokenResponse($"tok-{++calls}", 3600));
            using var sut = CreateSut(http, new FakeTimeProvider());

            await sut.GetTokenAsync(CancellationToken.None);
            sut.Invalidate();
            string next = await sut.GetTokenAsync(CancellationToken.None);

            calls.Should().Be(2);
            next.Should().Be("tok-2");
        }

        private static SoundCloudAppTokenCache CreateSut(HttpClient http, TimeProvider clock) =>
            new SoundCloudAppTokenCache(
                http,
                new SoundCloudEngagementSettings { ClientId = ClientId, ClientSecret = ClientSecret },
                clock,
                NullLogger<SoundCloudAppTokenCache>.Instance);

        private static HttpClient CountingClient(Func<HttpResponseMessage> respond) =>
            new HttpClient(new StubHttpMessageHandler((request, ct) => Task.FromResult(respond())));

        private static HttpResponseMessage TokenResponse(string token, int expiresIn) =>
            JsonResponse(HttpStatusCode.OK, $"{{\"access_token\":\"{token}\",\"token_type\":\"bearer\",\"expires_in\":{expiresIn}}}");

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

        private sealed class CapturedRequest
        {
            private CapturedRequest(HttpMethod method, string uri, string? authorization, string body)
            {
                Method = method;
                Uri = uri;
                Authorization = authorization;
                Body = body;
            }

            public HttpMethod Method { get; }

            public string Uri { get; }

            public string? Authorization { get; }

            public string Body { get; }

            public static async Task<CapturedRequest> FromAsync(HttpRequestMessage request)
            {
                string body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);

                return new CapturedRequest(
                    request.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization?.ToString(),
                    body);
            }
        }
    }
}
