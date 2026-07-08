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
    public sealed class FailMixLabRunUseCaseTests
    {
        [Test]
        public async Task FailAsync_on_running_run_marks_failed_and_stores_error()
        {
            (FailMixLabRunUseCase sut, BlobMixLabRunRepository runs) = BuildSut();
            string runId = await ClaimedRunAsync(runs);

            FailMixLabRunResult result = await sut.FailAsync(runId, "boom", null, CancellationToken.None);

            result.Outcome.Should().Be(FailMixLabRunResult.FailOutcome.Failed);
            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Status.Should().Be(MixLabRunStatus.Failed);
            run.Error.Should().Contain("boom");
        }

        [Test]
        public async Task FailAsync_on_already_failed_run_is_idempotent()
        {
            (FailMixLabRunUseCase sut, BlobMixLabRunRepository runs) = BuildSut();
            string runId = await ClaimedRunAsync(runs);
            await sut.FailAsync(runId, "boom", null, CancellationToken.None);

            FailMixLabRunResult second = await sut.FailAsync(runId, "again", null, CancellationToken.None);

            second.Outcome.Should().Be(FailMixLabRunResult.FailOutcome.AlreadyFailed);
            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Error.Should().Contain("boom");
            run.Error.Should().NotContain("again");
        }

        [Test]
        public async Task FailAsync_on_queued_run_is_conflict()
        {
            (FailMixLabRunUseCase sut, BlobMixLabRunRepository runs) = BuildSut();
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);

            FailMixLabRunResult result = await sut.FailAsync(created.RunId, "boom", null, CancellationToken.None);

            result.Outcome.Should().Be(FailMixLabRunResult.FailOutcome.Conflict);
        }

        [Test]
        public async Task FailAsync_on_succeeded_run_is_conflict()
        {
            (FailMixLabRunUseCase sut, BlobMixLabRunRepository runs) = BuildSut();
            string runId = await ClaimedRunAsync(runs);
            await runs.CompleteAsync(runId, Array.Empty<MixLabRunConcept>(), CancellationToken.None);

            FailMixLabRunResult result = await sut.FailAsync(runId, "boom", null, CancellationToken.None);

            result.Outcome.Should().Be(FailMixLabRunResult.FailOutcome.Conflict);
        }

        [Test]
        public async Task FailAsync_unknown_run_is_not_found()
        {
            (FailMixLabRunUseCase sut, _) = BuildSut();

            FailMixLabRunResult result = await sut.FailAsync("r_unknown", "boom", null, CancellationToken.None);

            result.Outcome.Should().Be(FailMixLabRunResult.FailOutcome.NotFound);
        }

        [Test]
        public async Task FailAsync_truncates_log_tail_to_8_kb()
        {
            (FailMixLabRunUseCase sut, BlobMixLabRunRepository runs) = BuildSut();
            string runId = await ClaimedRunAsync(runs);
            string logTail = new string('x', 10_000);

            await sut.FailAsync(runId, "boom", logTail, CancellationToken.None);

            MixLabRun? run = await runs.GetAsync(runId, CancellationToken.None);
            run!.Error.Should().Contain("boom");
            run.Error!.Count(c => c == 'x').Should().Be(8 * 1024);
        }

        private static (FailMixLabRunUseCase Sut, BlobMixLabRunRepository Runs) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var sut = new FailMixLabRunUseCase(runs, NullLogger<FailMixLabRunUseCase>.Instance);
            return (sut, runs);
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
    }
}
