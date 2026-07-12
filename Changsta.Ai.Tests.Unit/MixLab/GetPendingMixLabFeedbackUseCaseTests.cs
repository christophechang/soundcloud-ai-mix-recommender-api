using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class GetPendingMixLabFeedbackUseCaseTests
    {
        [Test]
        public async Task GetPendingAsync_no_events_returns_empty()
        {
            (GetPendingMixLabFeedbackUseCase sut, _) = BuildSut();

            IReadOnlyList<MixLabFeedbackEvent> pending = await sut.GetPendingAsync(CancellationToken.None);

            pending.Should().BeEmpty();
        }

        [Test]
        public async Task GetPendingAsync_returns_appended_events_verbatim()
        {
            (GetPendingMixLabFeedbackUseCase sut, BlobMixLabFeedbackQueue queue) = BuildSut();
            var feedbackEvent = new MixLabFeedbackEvent
            {
                EventId = "f_1",
                RunId = "r_1",
                ConceptId = "concept-1",
                Verdict = MixLabFeedbackVerdict.Played,
                RecordedAt = DateTimeOffset.UtcNow,
            };
            await queue.AppendAsync(feedbackEvent, CancellationToken.None);

            IReadOnlyList<MixLabFeedbackEvent> pending = await sut.GetPendingAsync(CancellationToken.None);

            pending.Should().ContainSingle(e => e.EventId == "f_1");
        }

        private static (GetPendingMixLabFeedbackUseCase Sut, BlobMixLabFeedbackQueue Queue) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var queue = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);
            var sut = new GetPendingMixLabFeedbackUseCase(queue);
            return (sut, queue);
        }
    }
}
