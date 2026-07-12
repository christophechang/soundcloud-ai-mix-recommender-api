using System;
using System.Text.Json.Nodes;
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
    public sealed class DeleteMixLabRunUseCaseTests
    {
        [Test]
        public async Task DeleteAsync_unknown_run_returns_not_found()
        {
            (DeleteMixLabRunUseCase sut, _, _, _) = BuildSut();

            DeleteMixLabRunResult result = await sut.DeleteAsync("r_missing", CancellationToken.None);

            result.Outcome.Should().Be(DeleteMixLabRunResult.DeleteOutcome.NotFound);
        }

        [Test]
        public async Task DeleteAsync_queued_run_is_rejected_as_active_and_not_deleted()
        {
            (DeleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun queued = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            DeleteMixLabRunResult result = await sut.DeleteAsync(queued.RunId, CancellationToken.None);

            result.Outcome.Should().Be(DeleteMixLabRunResult.DeleteOutcome.Active);
            (await runs.GetAsync(queued.RunId, CancellationToken.None)).Should().NotBeNull();
        }

        [Test]
        public async Task DeleteAsync_running_run_is_rejected_as_active()
        {
            (DeleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await runs.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            DeleteMixLabRunResult result = await sut.DeleteAsync(created.RunId, CancellationToken.None);

            result.Outcome.Should().Be(DeleteMixLabRunResult.DeleteOutcome.Active);
        }

        [Test]
        public async Task DeleteAsync_succeeded_run_deletes_run_and_purges_only_its_history_entry()
        {
            (DeleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabHistoryStore history, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun target = await CompleteRunAsync(runs, "u_1", time);
            MixLabRun kept = await CompleteRunAsync(runs, "u_2", time);

            await history.PutAsync(HistoryJson(target.RunId, kept.RunId), ifMatchETag: null, CancellationToken.None);

            DeleteMixLabRunResult result = await sut.DeleteAsync(target.RunId, CancellationToken.None);

            result.Outcome.Should().Be(DeleteMixLabRunResult.DeleteOutcome.Deleted);
            (await runs.GetAsync(target.RunId, CancellationToken.None)).Should().BeNull();

            MixLabHistorySnapshot? snapshot = await history.GetAsync(CancellationToken.None);
            snapshot.Should().NotBeNull();
            snapshot!.Content.Should().NotContain(target.RunId);
            snapshot.Content.Should().Contain(kept.RunId);
        }

        [Test]
        public async Task DeleteAsync_succeeds_when_no_history_document_exists()
        {
            (DeleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun target = await CompleteRunAsync(runs, "u_1", time);

            DeleteMixLabRunResult result = await sut.DeleteAsync(target.RunId, CancellationToken.None);

            result.Outcome.Should().Be(DeleteMixLabRunResult.DeleteOutcome.Deleted);
        }

        private static (DeleteMixLabRunUseCase Sut, BlobMixLabRunRepository Runs, BlobMixLabHistoryStore History, FakeTimeProvider Time) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var history = new BlobMixLabHistoryStore(gateway);
            var sut = new DeleteMixLabRunUseCase(runs, history, NullLogger<DeleteMixLabRunUseCase>.Instance);
            return (sut, runs, history, time);
        }

        private static async Task<MixLabRun> CompleteRunAsync(BlobMixLabRunRepository runs, string uploadId, FakeTimeProvider time)
        {
            time.UtcNow = time.UtcNow.AddMinutes(1);
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), uploadId, CancellationToken.None);
            await runs.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await runs
                .CompleteAsync(created.RunId, new[] { new MixLabRunConcept { ConceptId = "c1", Title = "Title" } }, CancellationToken.None)
                .ConfigureAwait(false);
            return created;
        }

        private static MixLabRunFlags MakeFlags()
        {
            return new MixLabRunFlags
            {
                Genre = "traverse",
                Mode = "all",
                Risk = "high",
                Directions = "mixed",
            };
        }

        private static string HistoryJson(params string[] runIds)
        {
            var runsArray = new JsonArray();
            foreach (string id in runIds)
            {
                runsArray.Add(new JsonObject { ["run_id"] = id, ["genre"] = "dnb" });
            }

            return new JsonObject { ["runs"] = runsArray }.ToJsonString();
        }
    }
}
