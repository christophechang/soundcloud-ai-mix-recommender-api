using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    public sealed class CompleteMixLabRunUseCaseTests
    {
        private const string SummaryWithTwoConcepts =
            "{\"concepts\":[{\"conceptId\":\"c1\",\"title\":\"First\"},{\"conceptId\":\"c2\",\"title\":\"Second\"}]}";

        [Test]
        public async Task CompleteAsync_running_run_succeeds_and_extracts_concepts()
        {
            (CompleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, _) = BuildSut();
            string runId = await ClaimedRunAsync(runs);

            CompleteMixLabRunResult result = await sut.CompleteAsync(
                runId, Text(SummaryWithTwoConcepts), Text("<html/>"), null, CancellationToken.None);

            result.Outcome.Should().Be(CompleteMixLabRunResult.CompleteOutcome.Completed);

            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Status.Should().Be(MixLabRunStatus.Succeeded);
            run.CompletedAt.Should().NotBeNull();
            run.Concepts.Select(c => c.ConceptId).Should().Equal("c1", "c2");
            run.Concepts.Select(c => c.Title).Should().Equal("First", "Second");

            IReadOnlyList<MixLabRunIndexEntry> index = await runs.GetIndexAsync(10, 0, CancellationToken.None);
            index.Single(e => e.RunId == runId).ConceptCount.Should().Be(2);
        }

        [Test]
        public async Task CompleteAsync_without_export_completes_and_stores_no_export()
        {
            (CompleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, FakeMixLabBlobGateway gateway) = BuildSut();
            string runId = await ClaimedRunAsync(runs);

            CompleteMixLabRunResult result = await sut.CompleteAsync(
                runId, Text(SummaryWithTwoConcepts), Text("<html/>"), null, CancellationToken.None);

            result.Outcome.Should().Be(CompleteMixLabRunResult.CompleteOutcome.Completed);
            gateway.WrittenPaths.Should().Contain($"runs/{runId}/summary.json");
            gateway.WrittenPaths.Should().Contain($"runs/{runId}/report.html");
            gateway.WrittenPaths.Should().NotContain($"runs/{runId}/export.xml");
        }

        [Test]
        public async Task CompleteAsync_with_export_stores_the_export_artifact()
        {
            (CompleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, BlobMixLabArtifactStore artifacts, _) = BuildSut();
            string runId = await ClaimedRunAsync(runs);

            await sut.CompleteAsync(
                runId, Text(SummaryWithTwoConcepts), Text("<html/>"), Text("<DJ_PLAYLISTS/>"), CancellationToken.None);

            await using Stream export = await artifacts.OpenReadAsync(runId, "export.xml", CancellationToken.None);
            using var reader = new StreamReader(export);
            (await reader.ReadToEndAsync()).Should().Be("<DJ_PLAYLISTS/>");
        }

        [Test]
        public async Task CompleteAsync_on_already_succeeded_is_idempotent_no_op()
        {
            (CompleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, FakeMixLabBlobGateway gateway) = BuildSut();
            string runId = await ClaimedRunAsync(runs);

            await sut.CompleteAsync(runId, Text(SummaryWithTwoConcepts), Text("<html/>"), null, CancellationToken.None);
            int writesAfterFirst = gateway.WrittenPaths.Count;

            CompleteMixLabRunResult second = await sut.CompleteAsync(
                runId,
                Text("{\"concepts\":[{\"conceptId\":\"other\",\"title\":\"Other\"}]}"),
                Text("<html>different</html>"),
                null,
                CancellationToken.None);

            second.Outcome.Should().Be(CompleteMixLabRunResult.CompleteOutcome.AlreadyCompleted);
            gateway.WrittenPaths.Count.Should().Be(writesAfterFirst);

            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Concepts.Select(c => c.ConceptId).Should().Equal("c1", "c2");
        }

        [Test]
        public async Task CompleteAsync_on_queued_run_is_conflict()
        {
            (CompleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, _) = BuildSut();
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            CompleteMixLabRunResult result = await sut.CompleteAsync(
                created.RunId, Text(SummaryWithTwoConcepts), Text("<html/>"), null, CancellationToken.None);

            result.Outcome.Should().Be(CompleteMixLabRunResult.CompleteOutcome.Conflict);
        }

        [Test]
        public async Task CompleteAsync_unknown_run_is_not_found()
        {
            (CompleteMixLabRunUseCase sut, _, _, _) = BuildSut();

            CompleteMixLabRunResult result = await sut.CompleteAsync(
                "r_unknown", Text(SummaryWithTwoConcepts), Text("<html/>"), null, CancellationToken.None);

            result.Outcome.Should().Be(CompleteMixLabRunResult.CompleteOutcome.NotFound);
        }

        [Test]
        public async Task CompleteAsync_invalid_summary_json_is_rejected_without_side_effects()
        {
            (CompleteMixLabRunUseCase sut, BlobMixLabRunRepository runs, _, FakeMixLabBlobGateway gateway) = BuildSut();
            string runId = await ClaimedRunAsync(runs);
            int writesBefore = gateway.WrittenPaths.Count;

            CompleteMixLabRunResult result = await sut.CompleteAsync(
                runId, Text("{not json"), Text("<html/>"), null, CancellationToken.None);

            result.Outcome.Should().Be(CompleteMixLabRunResult.CompleteOutcome.InvalidSummary);
            gateway.WrittenPaths.Count.Should().Be(writesBefore);
            (await runs.GetAsync(runId, CancellationToken.None))!.Status.Should().Be(MixLabRunStatus.Running);
        }

        private static (CompleteMixLabRunUseCase Sut, BlobMixLabRunRepository Runs, BlobMixLabArtifactStore Artifacts, FakeMixLabBlobGateway Gateway) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var artifacts = new BlobMixLabArtifactStore(gateway);
            var sut = new CompleteMixLabRunUseCase(runs, artifacts, NullLogger<CompleteMixLabRunUseCase>.Instance);
            return (sut, runs, artifacts, gateway);
        }

        private static async Task<string> ClaimedRunAsync(BlobMixLabRunRepository runs)
        {
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await runs.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
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

        private static MemoryStream Text(string content)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }
    }
}
