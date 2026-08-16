using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class SetMixLabConceptShortlistUseCaseTests
    {
        [Test]
        public async Task SetAsync_marks_the_concept_and_reports_updated()
        {
            var sut = BuildSut(out var runs, out _);
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SetMixLabConceptShortlistResult result = await sut.SetAsync(runId, "concept-1", true, CancellationToken.None);

            result.Outcome.Should().Be(SetMixLabConceptShortlistResult.SetOutcome.Updated);
            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Concepts.Single().Shortlisted.Should().BeTrue();
        }

        [Test]
        public async Task SetAsync_never_writes_a_feedback_event()
        {
            // Shortlisting is web/API-only: nothing about it may reach engine history via the
            // pending-feedback queue.
            var sut = BuildSut(out var runs, out var gateway);
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            await sut.SetAsync(runId, "concept-1", true, CancellationToken.None);

            gateway.WrittenPaths.Should().NotContain("feedback/pending.json");
        }

        [Test]
        public async Task SetAsync_clearing_an_unstarred_concept_is_idempotent()
        {
            var sut = BuildSut(out var runs, out _);
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SetMixLabConceptShortlistResult result = await sut.SetAsync(runId, "concept-1", false, CancellationToken.None);

            result.Outcome.Should().Be(SetMixLabConceptShortlistResult.SetOutcome.Updated);
            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Concepts.Single().Shortlisted.Should().BeFalse();
        }

        [Test]
        public async Task SetAsync_unknown_run_is_run_not_found()
        {
            var sut = BuildSut(out _, out _);

            SetMixLabConceptShortlistResult result = await sut.SetAsync("r_missing", "concept-1", true, CancellationToken.None);

            result.Outcome.Should().Be(SetMixLabConceptShortlistResult.SetOutcome.RunNotFound);
        }

        [Test]
        public async Task SetAsync_unknown_concept_is_concept_not_found()
        {
            var sut = BuildSut(out var runs, out _);
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SetMixLabConceptShortlistResult result = await sut.SetAsync(runId, "does-not-exist", true, CancellationToken.None);

            result.Outcome.Should().Be(SetMixLabConceptShortlistResult.SetOutcome.ConceptNotFound);
        }

        private static async Task<string> CompletedRunWithConceptAsync(BlobMixLabRunRepository runs, string conceptId)
        {
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await runs.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await runs.CompleteAsync(
                created.RunId,
                new[] { new MixLabRunConcept { ConceptId = conceptId, Title = "Title" } },
                CancellationToken.None);
            return created.RunId;
        }

        private static MixLabRunFlags MakeFlags()
        {
            return new MixLabRunFlags
            {
                Genre = "techno",
                Mode = "all",
                Risk = "high",
                Directions = "mixed",
            };
        }

        // out-parameter shape matches BlobMixLabRunRepositoryTests.BuildSut (line 325), the closest
        // fixture in this folder.
        private static SetMixLabConceptShortlistUseCase BuildSut(
            out BlobMixLabRunRepository runs,
            out FakeMixLabBlobGateway gateway)
        {
            gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            return new SetMixLabConceptShortlistUseCase(runs);
        }
    }
}
