using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement;
using Changsta.Ai.Tests.Unit.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Engagement
{
    [TestFixture]
    public sealed class CachedMixEngagementProviderTests
    {
        private const string RssUrl = "https://feeds.soundcloud.com/users/soundcloud:users:123456/sounds.rss";
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

        [Test]
        public async Task GetAllAsync_returns_null_without_fetch_when_disabled()
        {
            var client = new FakeClient();
            SoundCloudEngagementSettings settings = Settings();
            settings.StatsEnabled = false;
            var sut = new CachedMixEngagementProvider(client, settings, new FakeTimeProvider(), Budget);

            IReadOnlyList<MixEngagement>? result = await sut.GetAllAsync(CancellationToken.None);

            result.Should().BeNull();
            client.Calls.Should().Be(0);
        }

        [TestCase(null, "secret")]
        [TestCase("id", null)]
        [TestCase("", "secret")]
        [TestCase("id", "   ")]
        public async Task GetAllAsync_returns_null_without_fetch_when_credentials_missing(string? clientId, string? clientSecret)
        {
            var client = new FakeClient();
            SoundCloudEngagementSettings settings = Settings();
            settings.ClientId = clientId;
            settings.ClientSecret = clientSecret;
            var sut = new CachedMixEngagementProvider(client, settings, new FakeTimeProvider(), Budget);

            IReadOnlyList<MixEngagement>? result = await sut.GetAllAsync(CancellationToken.None);

            result.Should().BeNull();
            client.Calls.Should().Be(0);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("https://feeds.soundcloud.com/users/changsta/sounds.rss")]
        public async Task GetAllAsync_returns_null_without_fetch_when_rss_url_has_no_user_id(string? rssUrl)
        {
            var client = new FakeClient();
            SoundCloudEngagementSettings settings = Settings();
            settings.RssUrl = rssUrl;
            var sut = new CachedMixEngagementProvider(client, settings, new FakeTimeProvider(), Budget);

            IReadOnlyList<MixEngagement>? result = await sut.GetAllAsync(CancellationToken.None);

            result.Should().BeNull();
            client.Calls.Should().Be(0);
        }

        [Test]
        public async Task GetAllAsync_passes_the_user_id_from_the_rss_url()
        {
            var client = new FakeClient();
            var sut = new CachedMixEngagementProvider(client, Settings(), new FakeTimeProvider(), Budget);

            await sut.GetAllAsync(CancellationToken.None);

            client.LastUserId.Should().Be(123456);
        }

        [Test]
        public async Task GetAllAsync_reuses_result_until_ttl_then_refetches()
        {
            var clock = new FakeTimeProvider();
            var client = new FakeClient();
            var sut = new CachedMixEngagementProvider(client, Settings(), clock, Budget);

            IReadOnlyList<MixEngagement>? first = await sut.GetAllAsync(CancellationToken.None);
            clock.UtcNow = clock.UtcNow.AddSeconds(599);
            IReadOnlyList<MixEngagement>? second = await sut.GetAllAsync(CancellationToken.None);

            client.Calls.Should().Be(1);
            second.Should().BeSameAs(first);

            clock.UtcNow = clock.UtcNow.AddSeconds(2);
            IReadOnlyList<MixEngagement>? third = await sut.GetAllAsync(CancellationToken.None);

            client.Calls.Should().Be(2);
            third.Should().NotBeNull();
            third.Should().NotBeSameAs(first);
        }

        [Test]
        public async Task GetAllAsync_holds_failure_for_sixty_seconds_then_recovers()
        {
            var clock = new FakeTimeProvider();
            var client = new FakeClient { NextResults = { null } };
            var sut = new CachedMixEngagementProvider(client, Settings(), clock, Budget);

            (await sut.GetAllAsync(CancellationToken.None)).Should().BeNull();
            clock.UtcNow = clock.UtcNow.AddSeconds(59);
            (await sut.GetAllAsync(CancellationToken.None)).Should().BeNull();
            client.Calls.Should().Be(1);

            clock.UtcNow = clock.UtcNow.AddSeconds(2);
            IReadOnlyList<MixEngagement>? recovered = await sut.GetAllAsync(CancellationToken.None);

            client.Calls.Should().Be(2);
            recovered.Should().NotBeNull();
            recovered!.Should().ContainSingle(e => e.TrackId == 2);
        }

        [Test]
        public async Task GetAllAsync_concurrent_callers_share_one_fetch()
        {
            var client = new FakeClient { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var sut = new CachedMixEngagementProvider(client, Settings(), new FakeTimeProvider(), Budget);

            Task<IReadOnlyList<MixEngagement>?>[] callers = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => sut.GetAllAsync(CancellationToken.None)))
                .ToArray();

            await client.Started.Task;
            client.Gate.SetResult();
            IReadOnlyList<MixEngagement>?[] results = await Task.WhenAll(callers);

            client.Calls.Should().Be(1);
            results.Should().OnlyContain(r => r != null && ReferenceEquals(r, results[0]));
        }

        [Test]
        public async Task GetAllAsync_cancelled_caller_does_not_cancel_shared_fetch()
        {
            var client = new FakeClient { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var sut = new CachedMixEngagementProvider(client, Settings(), new FakeTimeProvider(), Budget);

            using var cts = new CancellationTokenSource();
            Task<IReadOnlyList<MixEngagement>?> first = sut.GetAllAsync(cts.Token);
            await client.Started.Task;
            await cts.CancelAsync();

            Func<Task> awaitFirst = () => first;
            await awaitFirst.Should().ThrowAsync<OperationCanceledException>();
            client.ObservedCancellation.Should().BeFalse("the shared fetch runs under the provider's own token");

            client.Gate.SetResult();
            IReadOnlyList<MixEngagement>? second = await sut.GetAllAsync(CancellationToken.None);

            second.Should().NotBeNull();
            client.Calls.Should().Be(1);

            IReadOnlyList<MixEngagement>? third = await sut.GetAllAsync(CancellationToken.None);
            third.Should().BeSameAs(second, "the entry holds the success, not a failure");
            client.Calls.Should().Be(1);
        }

        [Test]
        public async Task GetAllAsync_fetch_over_budget_returns_null_and_holds_failure()
        {
            var clock = new FakeTimeProvider();
            var client = new FakeClient { WaitForCancellation = true };
            var sut = new CachedMixEngagementProvider(client, Settings(), clock, TimeSpan.FromMilliseconds(50));

            IReadOnlyList<MixEngagement>? result = await sut.GetAllAsync(CancellationToken.None);

            result.Should().BeNull();
            client.ObservedCancellation.Should().BeTrue();

            clock.UtcNow = clock.UtcNow.AddSeconds(30);
            (await sut.GetAllAsync(CancellationToken.None)).Should().BeNull();
            client.Calls.Should().Be(1);
        }

        [Test]
        public async Task GetAllAsync_unexpected_client_exception_is_held_as_failure()
        {
            var client = new FakeClient { Throw = new InvalidOperationException("boom") };
            var sut = new CachedMixEngagementProvider(client, Settings(), new FakeTimeProvider(), Budget);

            (await sut.GetAllAsync(CancellationToken.None)).Should().BeNull();
            (await sut.GetAllAsync(CancellationToken.None)).Should().BeNull();

            client.Calls.Should().Be(1);
        }

        private static SoundCloudEngagementSettings Settings() => new()
        {
            ClientId = "id",
            ClientSecret = "secret",
            RssUrl = RssUrl,
        };

        private sealed class FakeClient : ISoundCloudEngagementClient
        {
            private int _calls;

            public TaskCompletionSource? Gate { get; init; }

            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool WaitForCancellation { get; init; }

            public Exception? Throw { get; init; }

            public List<IReadOnlyList<MixEngagement>?> NextResults { get; } = new();

            public int Calls => Volatile.Read(ref _calls);

            public long LastUserId { get; private set; }

            public bool ObservedCancellation { get; private set; }

            public async Task<IReadOnlyList<MixEngagement>?> FetchAllAsync(long userId, CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref _calls);
                LastUserId = userId;
                Started.TrySetResult();

                if (Throw is not null)
                {
                    throw Throw;
                }

                if (WaitForCancellation)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        ObservedCancellation = true;
                        throw;
                    }
                }

                if (Gate is not null)
                {
                    await Gate.Task;
                }

                ObservedCancellation = cancellationToken.IsCancellationRequested;

                if (call <= NextResults.Count)
                {
                    return NextResults[call - 1];
                }

                return new[] { new MixEngagement { TrackId = call, Plays = call * 10, Likes = call } };
            }
        }
    }
}
