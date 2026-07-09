using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class SubmitMixLabConceptFeedbackUseCaseTests
    {
        [Test]
        public async Task SubmitAsync_all_fields_absent_is_invalid_request()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating: null, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest);
        }

        [TestCase("played")]
        [TestCase("played_modified")]
        [TestCase("rejected")]
        [TestCase("unused")]
        public async Task SubmitAsync_accepts_every_allowed_verdict_wire_value(string verdict)
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict, rating: null, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded);
        }

        [Test]
        public async Task SubmitAsync_unknown_verdict_is_invalid_request()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", "loved_it", rating: null, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest);
        }

        [TestCase(1)]
        [TestCase(5)]
        public async Task SubmitAsync_accepts_rating_within_range(int rating)
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded);
        }

        [TestCase(0)]
        [TestCase(6)]
        public async Task SubmitAsync_rejects_rating_out_of_range(int rating)
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest);
        }

        [Test]
        public async Task SubmitAsync_accepts_notes_at_max_length()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");
            string notes = new string('a', 2000);

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating: null, notes, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded);
        }

        [Test]
        public async Task SubmitAsync_rejects_notes_over_max_length()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");
            string notes = new string('a', 2001);

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating: null, notes, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest);
        }

        [Test]
        public async Task SubmitAsync_known_catalogue_slug_is_recorded()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut(
                catalogueMixes: new[] { MakeMix("https://soundcloud.com/changsta/known-mix") });
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating: null, notes: null, "known-mix", CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded);
        }

        [Test]
        public async Task SubmitAsync_unknown_catalogue_slug_is_invalid_request()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, _, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", verdict: null, rating: null, notes: null, "does-not-exist", CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest);
        }

        [Test]
        public async Task SubmitAsync_unknown_run_is_run_not_found_and_leaves_queue_untouched()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, _, BlobMixLabFeedbackQueue queue, _, _) = BuildSut();

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                "r_unknown", "concept-1", "played", rating: null, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.RunNotFound);
            (await queue.GetPendingAsync(CancellationToken.None)).Should().BeEmpty();
        }

        [Test]
        public async Task SubmitAsync_unknown_concept_is_concept_not_found_and_leaves_queue_untouched()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, BlobMixLabFeedbackQueue queue, _, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "does-not-exist", "played", rating: null, notes: null, publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.ConceptNotFound);
            (await queue.GetPendingAsync(CancellationToken.None)).Should().BeEmpty();
        }

        [Test]
        public async Task SubmitAsync_merges_feedback_onto_run_and_appends_pending_event()
        {
            (SubmitMixLabConceptFeedbackUseCase sut, BlobMixLabRunRepository runs, BlobMixLabFeedbackQueue queue, FakeTimeProvider time, _) = BuildSut();
            string runId = await CompletedRunWithConceptAsync(runs, "concept-1");
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            SubmitMixLabConceptFeedbackResult result = await sut.SubmitAsync(
                runId, "concept-1", "played_modified", 4, "Great transition", publishedMixSlug: null, CancellationToken.None);

            result.Outcome.Should().Be(SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded);

            MixLabRun? updated = await runs.GetAsync(runId, CancellationToken.None);
            MixLabConceptFeedback? feedback = updated!.Concepts.Single(c => c.ConceptId == "concept-1").Feedback;
            feedback.Should().NotBeNull();
            feedback!.Verdict.Should().Be(MixLabFeedbackVerdict.PlayedModified);
            feedback.Rating.Should().Be(4);
            feedback.Notes.Should().Be("Great transition");
            feedback.RecordedAt.Should().Be(time.UtcNow);

            IReadOnlyList<MixLabFeedbackEvent> pending = await queue.GetPendingAsync(CancellationToken.None);
            pending.Should().ContainSingle();
            MixLabFeedbackEvent queued = pending.Single();
            queued.RunId.Should().Be(runId);
            queued.ConceptId.Should().Be("concept-1");
            queued.Verdict.Should().Be(MixLabFeedbackVerdict.PlayedModified);
            queued.RecordedAt.Should().Be(time.UtcNow);
            queued.EventId.Should().NotBeNullOrEmpty();
        }

        private static Mix MakeMix(string url)
        {
            return new Mix
            {
                Id = url,
                Title = "Title",
                Url = url,
                Genre = "techno",
                Energy = "high",
            };
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

        private static (
            SubmitMixLabConceptFeedbackUseCase Sut,
            BlobMixLabRunRepository Runs,
            BlobMixLabFeedbackQueue Queue,
            FakeTimeProvider Time,
            StubMixCatalogueProvider Catalogue) BuildSut(IReadOnlyList<Mix>? catalogueMixes = null)
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var queue = new BlobMixLabFeedbackQueue(gateway, NullLogger<BlobMixLabFeedbackQueue>.Instance);
            var catalogue = new StubMixCatalogueProvider(catalogueMixes ?? Array.Empty<Mix>());
            var sut = new SubmitMixLabConceptFeedbackUseCase(runs, queue, catalogue, time);
            return (sut, runs, queue, time, catalogue);
        }

        private sealed class StubMixCatalogueProvider : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;

            public StubMixCatalogueProvider(IReadOnlyList<Mix> mixes)
            {
                _mixes = mixes;
            }

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken) =>
                Task.FromResult(_mixes);
        }
    }
}
