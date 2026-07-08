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
    public sealed class AckMixLabFeedbackUseCaseTests
    {
        [Test]
        public async Task AckAsync_removes_only_the_matching_events()
        {
            (AckMixLabFeedbackUseCase sut, BlobMixLabFeedbackQueue queue) = BuildSut();
            await queue.AppendAsync(MakeEvent("f_1"), CancellationToken.None);
            await queue.AppendAsync(MakeEvent("f_2"), CancellationToken.None);

            await sut.AckAsync(new[] { "f_1" }, CancellationToken.None);

            IReadOnlyList<MixLabFeedbackEvent> pending = await queue.GetPendingAsync(CancellationToken.None);
            pending.Should().ContainSingle(e => e.EventId == "f_2");
        }

        [Test]
        public async Task AckAsync_unknown_ids_are_ignored()
        {
            (AckMixLabFeedbackUseCase sut, BlobMixLabFeedbackQueue queue) = BuildSut();
            await queue.AppendAsync(MakeEvent("f_1"), CancellationToken.None);

            await sut.AckAsync(new[] { "f_unknown" }, CancellationToken.None);

            IReadOnlyList<MixLabFeedbackEvent> pending = await queue.GetPendingAsync(CancellationToken.None);
            pending.Should().ContainSingle(e => e.EventId == "f_1");
        }

        [Test]
        public async Task AckAsync_empty_list_is_a_no_op()
        {
            (AckMixLabFeedbackUseCase sut, BlobMixLabFeedbackQueue queue) = BuildSut();
            await queue.AppendAsync(MakeEvent("f_1"), CancellationToken.None);

            await sut.AckAsync(Array.Empty<string>(), CancellationToken.None);

            IReadOnlyList<MixLabFeedbackEvent> pending = await queue.GetPendingAsync(CancellationToken.None);
            pending.Should().ContainSingle(e => e.EventId == "f_1");
        }

        [Test]
        public async Task AckAsync_double_ack_of_the_same_id_is_fine()
        {
            (AckMixLabFeedbackUseCase sut, BlobMixLabFeedbackQueue queue) = BuildSut();
            await queue.AppendAsync(MakeEvent("f_1"), CancellationToken.None);

            await sut.AckAsync(new[] { "f_1" }, CancellationToken.None);
            await sut.AckAsync(new[] { "f_1" }, CancellationToken.None);

            IReadOnlyList<MixLabFeedbackEvent> pending = await queue.GetPendingAsync(CancellationToken.None);
            pending.Should().BeEmpty();
        }

        private static MixLabFeedbackEvent MakeEvent(string eventId)
        {
            return new MixLabFeedbackEvent
            {
                EventId = eventId,
                RunId = "r_1",
                ConceptId = "concept-1",
                Verdict = MixLabFeedbackVerdict.Played,
                RecordedAt = DateTimeOffset.UtcNow,
            };
        }

        private static (AckMixLabFeedbackUseCase Sut, BlobMixLabFeedbackQueue Queue) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var queue = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);
            var sut = new AckMixLabFeedbackUseCase(queue);
            return (sut, queue);
        }
    }
}
