using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class BlobMixLabRunRepositoryTests
    {
        [Test]
        public async Task HasLiveRunningRunAsync_is_true_while_a_claim_is_within_the_lease()
        {
            var sut = BuildSut(out _, out var time);
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            time.UtcNow = time.UtcNow.AddMinutes(44);

            (await sut.HasLiveRunningRunAsync(TimeSpan.FromMinutes(45), CancellationToken.None)).Should().BeTrue();
        }

        [Test]
        public async Task HasLiveRunningRunAsync_is_false_once_the_claim_has_gone_stale()
        {
            var sut = BuildSut(out _, out var time);
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            time.UtcNow = time.UtcNow.AddMinutes(46);

            (await sut.HasLiveRunningRunAsync(TimeSpan.FromMinutes(45), CancellationToken.None)).Should().BeFalse();
        }

        [Test]
        public async Task HasLiveRunningRunAsync_is_false_for_queued_and_terminal_runs()
        {
            var sut = BuildSut(out _, out var time);
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun failed = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.FailAsync(failed.RunId, "boom", CancellationToken.None);
            await sut.CreateQueuedAsync(MakeFlags(), "u_2", CancellationToken.None);

            (await sut.HasLiveRunningRunAsync(TimeSpan.FromMinutes(45), CancellationToken.None)).Should().BeFalse();
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_no_runs_returns_null()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun? claimed = await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().BeNull();
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_only_non_queued_runs_returns_null()
        {
            var sut = BuildSut(out _, out var time);
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.FailAsync(created.RunId, "boom", CancellationToken.None);

            MixLabRun? claimed = await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().BeNull();
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_multiple_queued_returns_oldest_by_created_at()
        {
            var sut = BuildSut(out _, out var time);

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun older = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 5, 0, TimeSpan.Zero);
            await sut.CreateQueuedAsync(MakeFlags(), "u_2", CancellationToken.None);

            MixLabRun? claimed = await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().NotBeNull();
            claimed!.RunId.Should().Be(older.RunId);
            claimed.Status.Should().Be(MixLabRunStatus.Running);
            claimed.WorkerId.Should().Be("worker-1");
            claimed.ClaimedAt.Should().Be(time.UtcNow);
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_requeues_stale_running_run_then_claims_it()
        {
            var sut = BuildSut(out _, out var time);

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            // Advance past the 45-minute stale-claim lease.
            time.UtcNow = time.UtcNow.AddMinutes(46);

            MixLabRun? reclaimed = await sut.TryClaimOldestQueuedAsync("worker-2", TimeSpan.FromMinutes(45), CancellationToken.None);

            reclaimed.Should().NotBeNull();
            reclaimed!.RunId.Should().Be(created.RunId);
            reclaimed.WorkerId.Should().Be("worker-2");
            reclaimed.Status.Should().Be(MixLabRunStatus.Running);
            reclaimed.ClaimedAt.Should().Be(time.UtcNow);
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_running_run_within_lease_is_not_requeued()
        {
            var sut = BuildSut(out _, out var time);

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            time.UtcNow = time.UtcNow.AddMinutes(10);

            MixLabRun? claimed = await sut.TryClaimOldestQueuedAsync("worker-2", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().BeNull();
        }

        [Test]
        public async Task CompleteAsync_already_succeeded_is_idempotent_and_does_not_rewrite()
        {
            var sut = BuildSut(out var gateway, out var time);

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            var firstConcepts = new[] { MakeConcept("concept-1", "First Title") };
            await sut.CompleteAsync(created.RunId, firstConcepts, CancellationToken.None);

            int writesAfterFirstComplete = gateway.WrittenPaths.Count;

            var secondConcepts = new[] { MakeConcept("concept-2", "Different Title") };
            await sut.CompleteAsync(created.RunId, secondConcepts, CancellationToken.None);

            gateway.WrittenPaths.Count.Should().Be(writesAfterFirstComplete);

            MixLabRun? afterSecondComplete = await sut.GetAsync(created.RunId, CancellationToken.None);
            afterSecondComplete!.Concepts.Should().ContainSingle(c => c.ConceptId == "concept-1");
        }

        [Test]
        public async Task CompleteAsync_on_queued_run_throws_invalid_run_state()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            Func<Task> act = () => sut.CompleteAsync(created.RunId, Array.Empty<MixLabRunConcept>(), CancellationToken.None);

            await act.Should().ThrowAsync<MixLabInvalidRunStateException>()
                .Where(ex => ex.RunId == created.RunId);
        }

        [Test]
        public async Task CompleteAsync_retries_through_transient_conflicts_then_succeeds()
        {
            var sut = BuildSut(out var gateway, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            // Fewer forced conflicts than the bounded retry budget: should still succeed.
            gateway.ForcedConflictsRemaining = 2;

            var concepts = new[] { MakeConcept("concept-1", "Title") };
            await sut.CompleteAsync(created.RunId, concepts, CancellationToken.None);

            MixLabRun? completed = await sut.GetAsync(created.RunId, CancellationToken.None);
            completed!.Status.Should().Be(MixLabRunStatus.Succeeded);
            completed.Concepts.Should().ContainSingle(c => c.ConceptId == "concept-1");
        }

        [Test]
        public async Task CompleteAsync_exhausts_retries_and_surfaces_concurrency_exception()
        {
            var sut = BuildSut(out var gateway, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            // More forced conflicts than the bounded retry budget: must surface, not silently drop.
            gateway.ForcedConflictsRemaining = 10;

            Func<Task> act = () => sut.CompleteAsync(
                created.RunId,
                new[] { MakeConcept("concept-1", "Title") },
                CancellationToken.None);

            await act.Should().ThrowAsync<MixLabConcurrencyException>();
        }

        [Test]
        public async Task UpdateConceptFeedbackAsync_unknown_concept_throws_invalid_run_state()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await sut.CompleteAsync(created.RunId, new[] { MakeConcept("concept-1", "Title") }, CancellationToken.None);

            var feedback = new MixLabConceptFeedback { RecordedAt = DateTimeOffset.UtcNow };

            Func<Task> act = () => sut.UpdateConceptFeedbackAsync(created.RunId, "does-not-exist", feedback, CancellationToken.None);

            await act.Should().ThrowAsync<MixLabInvalidRunStateException>();
        }

        [Test]
        public async Task UpdateConceptFeedbackAsync_merges_feedback_onto_matching_concept()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await sut.CompleteAsync(created.RunId, new[] { MakeConcept("concept-1", "Title") }, CancellationToken.None);

            var feedback = new MixLabConceptFeedback
            {
                Verdict = MixLabFeedbackVerdict.PlayedModified,
                Rating = 4,
                Notes = "Great transition",
                RecordedAt = DateTimeOffset.UtcNow,
            };

            await sut.UpdateConceptFeedbackAsync(created.RunId, "concept-1", feedback, CancellationToken.None);

            MixLabRun? updated = await sut.GetAsync(created.RunId, CancellationToken.None);
            updated!.Concepts.Should().ContainSingle(c => c.ConceptId == "concept-1"
                && c.Feedback != null
                && c.Feedback.Verdict == MixLabFeedbackVerdict.PlayedModified
                && c.Feedback.Rating == 4);
        }

        [Test]
        public async Task CreateQueuedAsync_run_with_trackPool_flag_summary_ends_with_block()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(trackPool: "{\"conceptId\":\"c_1\"}"), "u_1", CancellationToken.None);

            IReadOnlyList<MixLabRunIndexEntry> index = await sut.GetIndexAsync(take: 100, skip: 0, CancellationToken.None);
            MixLabRunIndexEntry entry = index.Single(e => e.RunId == created.RunId);

            entry.FlagsSummary.Should().Be("all/high/mixed/block");
        }

        [Test]
        public async Task CreateQueuedAsync_run_without_trackPool_flag_summary_unchanged()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            IReadOnlyList<MixLabRunIndexEntry> index = await sut.GetIndexAsync(take: 100, skip: 0, CancellationToken.None);
            MixLabRunIndexEntry entry = index.Single(e => e.RunId == created.RunId);

            entry.FlagsSummary.Should().Be("all/high/mixed");
        }

        [Test]
        public async Task GetIndexAsync_applies_take_and_skip()
        {
            var sut = BuildSut(out _, out var time);

            for (int i = 0; i < 3; i++)
            {
                time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, i, 0, TimeSpan.Zero);
                await sut.CreateQueuedAsync(MakeFlags(), $"u_{i}", CancellationToken.None);
            }

            IReadOnlyList<MixLabRunIndexEntry> page = await sut.GetIndexAsync(take: 1, skip: 1, CancellationToken.None);

            page.Should().HaveCount(1);
        }

        [Test]
        public async Task DeleteAsync_removes_manifest_and_index_entry_leaving_other_runs()
        {
            var sut = BuildSut(out var gateway, out var time);

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun kept = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            time.UtcNow = time.UtcNow.AddMinutes(1);
            MixLabRun target = await sut.CreateQueuedAsync(MakeFlags(), "u_2", CancellationToken.None);

            await sut.DeleteAsync(target.RunId, CancellationToken.None);

            (await sut.GetAsync(target.RunId, CancellationToken.None)).Should().BeNull();
            gateway.DeletedPaths.Should().Contain($"runs/{target.RunId}/run.json");

            IReadOnlyList<MixLabRunIndexEntry> index = await sut.GetIndexAsync(take: 100, skip: 0, CancellationToken.None);
            index.Should().ContainSingle(e => e.RunId == kept.RunId);
            index.Should().NotContain(e => e.RunId == target.RunId);
        }

        [Test]
        public async Task DeleteAsync_unknown_run_is_a_no_op()
        {
            var sut = BuildSut(out _, out _);

            Func<Task> act = () => sut.DeleteAsync("r_does_not_exist", CancellationToken.None);

            await act.Should().NotThrowAsync();
        }

        [Test]
        public async Task GetIndexAsync_legacy_entry_without_counts_reads_zero()
        {
            // Every index entry written before this feature lacks both count fields; they must
            // deserialise as 0 rather than throwing, because the reindex is what fills them in.
            var sut = BuildSut(out var gateway, out _);
            byte[] legacy = Encoding.UTF8.GetBytes(
                "[{\"runId\":\"r_legacy\",\"createdAt\":\"2026-08-01T00:00:00+00:00\",\"status\":\"succeeded\"," +
                "\"genre\":\"techno\",\"flagsSummary\":\"all/high/mixed\",\"conceptCount\":2}]");
            await gateway.WriteUnconditionalAsync("runs/index.json", legacy, CancellationToken.None);

            IReadOnlyList<MixLabRunIndexEntry> entries = await sut.GetIndexAsync(take: 100, skip: 0, CancellationToken.None);

            MixLabRunIndexEntry entry = entries.Single();
            entry.RunId.Should().Be("r_legacy");
            entry.ShortlistedCount.Should().Be(0);
            entry.PlayedCount.Should().Be(0);
        }

        [Test]
        public async Task GetAsync_legacy_manifest_concept_without_shortlisted_reads_false()
        {
            var sut = BuildSut(out var gateway, out _);
            byte[] legacy = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"runId\":\"r_legacy\",\"createdAt\":\"2026-08-01T00:00:00+00:00\"," +
                "\"status\":\"succeeded\",\"flags\":{\"genre\":\"techno\",\"mode\":\"all\",\"risk\":\"high\"," +
                "\"directions\":\"mixed\"},\"uploadId\":\"u_1\"," +
                "\"concepts\":[{\"conceptId\":\"c_1\",\"title\":\"Title\"}]}");
            await gateway.WriteUnconditionalAsync("runs/r_legacy/run.json", legacy, CancellationToken.None);

            MixLabRun? run = await sut.GetAsync("r_legacy", CancellationToken.None);

            run!.Concepts.Single().Shortlisted.Should().BeFalse();
        }

        [Test]
        public async Task CompleteAsync_round_trips_shortlisted_true()
        {
            var sut = BuildSut(out _, out _);
            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await sut.CompleteAsync(
                created.RunId,
                new[] { MakeConcept("concept-1", "Title") with { Shortlisted = true } },
                CancellationToken.None);

            MixLabRun? stored = await sut.GetAsync(created.RunId, CancellationToken.None);

            stored!.Concepts.Single().Shortlisted.Should().BeTrue();
        }

        [Test]
        public async Task CreateQueuedAsync_index_entry_starts_with_zero_counts()
        {
            var sut = BuildSut(out _, out _);

            MixLabRun created = await sut.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            IReadOnlyList<MixLabRunIndexEntry> index = await sut.GetIndexAsync(take: 100, skip: 0, CancellationToken.None);
            MixLabRunIndexEntry entry = index.Single(e => e.RunId == created.RunId);
            entry.ShortlistedCount.Should().Be(0);
            entry.PlayedCount.Should().Be(0);
        }

        private static BlobMixLabRunRepository BuildSut(out FakeMixLabBlobGateway gateway, out FakeTimeProvider timeProvider)
        {
            gateway = new FakeMixLabBlobGateway();
            timeProvider = new FakeTimeProvider();
            return new BlobMixLabRunRepository(gateway, timeProvider, NullLogger<BlobMixLabRunRepository>.Instance);
        }

        private static MixLabRunFlags MakeFlags(string? trackPool = null)
        {
            return new MixLabRunFlags
            {
                Genre = "traverse",
                Mode = "all",
                Risk = "high",
                Directions = "mixed",
                TrackPool = trackPool,
            };
        }

        private static MixLabRunConcept MakeConcept(string conceptId, string title)
        {
            return new MixLabRunConcept { ConceptId = conceptId, Title = title };
        }
    }
}
