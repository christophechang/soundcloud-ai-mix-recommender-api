using System;
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
    public sealed class ClaimMixLabRunUseCaseTests
    {
        [Test]
        public async Task ClaimAsync_empty_queue_returns_null()
        {
            (ClaimMixLabRunUseCase sut, _, _) = BuildSut();

            MixLabRun? claimed = await sut.ClaimAsync("worker-1", CancellationToken.None);

            claimed.Should().BeNull();
        }

        [Test]
        public async Task ClaimAsync_returns_oldest_queued_and_marks_it_running()
        {
            (ClaimMixLabRunUseCase sut, BlobMixLabRunRepository runs, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun older = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            time.UtcNow = time.UtcNow.AddMinutes(5);
            await runs.CreateQueuedAsync(MakeFlags(), "u_2", CancellationToken.None);

            MixLabRun? claimed = await sut.ClaimAsync("worker-1", CancellationToken.None);

            claimed!.RunId.Should().Be(older.RunId);
            claimed.Status.Should().Be(MixLabRunStatus.Running);
            claimed.WorkerId.Should().Be("worker-1");
        }

        [Test]
        public async Task ClaimAsync_requeues_stale_running_run_then_reclaims_after_lease()
        {
            (ClaimMixLabRunUseCase sut, BlobMixLabRunRepository runs, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await sut.ClaimAsync("worker-1", CancellationToken.None);

            // Within the 45-minute lease nothing is reclaimable.
            time.UtcNow = time.UtcNow.AddMinutes(10);
            (await sut.ClaimAsync("worker-2", CancellationToken.None)).Should().BeNull();

            // Past the lease the stale run is requeued and claimable by the next worker.
            time.UtcNow = time.UtcNow.AddMinutes(40);
            MixLabRun? reclaimed = await sut.ClaimAsync("worker-2", CancellationToken.None);

            reclaimed!.RunId.Should().Be(created.RunId);
            reclaimed.WorkerId.Should().Be("worker-2");
            reclaimed.Status.Should().Be(MixLabRunStatus.Running);
        }

        private static (ClaimMixLabRunUseCase Sut, BlobMixLabRunRepository Runs, FakeTimeProvider Time) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var sut = new ClaimMixLabRunUseCase(runs, new MixLabOptions { ClaimLeaseMinutes = 45 });
            return (sut, runs, time);
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
