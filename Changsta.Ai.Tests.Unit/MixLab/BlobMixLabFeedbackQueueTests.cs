using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class BlobMixLabFeedbackQueueTests
    {
        [Test]
        public async Task AppendAsync_then_GetPendingAsync_returns_the_appended_event()
        {
            var gateway = new FakeMixLabBlobGateway();
            var sut = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);

            var feedbackEvent = MakeEvent("f_1");
            await sut.AppendAsync(feedbackEvent, CancellationToken.None);

            var pending = await sut.GetPendingAsync(CancellationToken.None);

            pending.Should().ContainSingle(e => e.EventId == "f_1");
        }

        [Test]
        public async Task AckAsync_removes_acked_events_and_leaves_others_pending()
        {
            var gateway = new FakeMixLabBlobGateway();
            var sut = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);

            await sut.AppendAsync(MakeEvent("f_1"), CancellationToken.None);
            await sut.AppendAsync(MakeEvent("f_2"), CancellationToken.None);

            await sut.AckAsync(new[] { "f_1" }, CancellationToken.None);

            var pending = await sut.GetPendingAsync(CancellationToken.None);

            pending.Should().ContainSingle(e => e.EventId == "f_2");
        }

        [Test]
        public async Task GetPendingAsync_no_blob_returns_empty()
        {
            var gateway = new FakeMixLabBlobGateway();
            var sut = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);

            var pending = await sut.GetPendingAsync(CancellationToken.None);

            pending.Should().BeEmpty();
        }

        [Test]
        public async Task RemoveForRunAsync_drops_only_the_named_run_events()
        {
            var gateway = new FakeMixLabBlobGateway();
            var sut = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);

            await sut.AppendAsync(MakeEvent("f_1"), CancellationToken.None);
            await sut.AppendAsync(MakeEvent("f_2", "r_2"), CancellationToken.None);

            await sut.RemoveForRunAsync("r_1", CancellationToken.None);

            var pending = await sut.GetPendingAsync(CancellationToken.None);

            pending.Should().ContainSingle(e => e.EventId == "f_2");
        }

        [Test]
        public async Task RemoveForRunAsync_writes_nothing_when_the_run_has_no_pending_events()
        {
            var gateway = new FakeMixLabBlobGateway();
            var sut = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);

            await sut.AppendAsync(MakeEvent("f_1"), CancellationToken.None);
            gateway.WrittenPaths.Clear();

            await sut.RemoveForRunAsync("r_other", CancellationToken.None);

            // No rewrite of an identical document — that would churn the ETag under a worker ack.
            gateway.WrittenPaths.Should().BeEmpty();
            (await sut.GetPendingAsync(CancellationToken.None)).Should().ContainSingle();
        }

        private static MixLabFeedbackEvent MakeEvent(string eventId, string runId = "r_1")
        {
            return new MixLabFeedbackEvent
            {
                EventId = eventId,
                RunId = runId,
                ConceptId = "concept-1",
                Verdict = MixLabFeedbackVerdict.Played,
                RecordedAt = DateTimeOffset.UtcNow,
            };
        }
    }
}
