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
    public sealed class ClaimMixLabMapUseCaseTests
    {
        [Test]
        public async Task ClaimAsync_empty_queue_returns_null()
        {
            (ClaimMixLabMapUseCase sut, _, _) = BuildSut();

            MixLabMapJob? claimed = await sut.ClaimAsync("worker-1", CancellationToken.None);

            claimed.Should().BeNull();
        }

        [Test]
        public async Task ClaimAsync_returns_oldest_queued_and_marks_it_running()
        {
            (ClaimMixLabMapUseCase sut, BlobMixLabMapRepository maps, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabMapJob older = await maps.RequestAsync("u_1", CancellationToken.None);
            time.UtcNow = time.UtcNow.AddMinutes(5);
            await maps.RequestAsync("u_2", CancellationToken.None);

            MixLabMapJob? claimed = await sut.ClaimAsync("worker-1", CancellationToken.None);

            claimed!.UploadId.Should().Be(older.UploadId);
            claimed.Status.Should().Be(MixLabMapStatus.Running);
            claimed.WorkerId.Should().Be("worker-1");
        }

        [Test]
        public async Task ClaimAsync_requeues_stale_running_job_then_reclaims_after_lease()
        {
            (ClaimMixLabMapUseCase sut, BlobMixLabMapRepository maps, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabMapJob created = await maps.RequestAsync("u_1", CancellationToken.None);
            await sut.ClaimAsync("worker-1", CancellationToken.None);

            // Within the 45-minute lease nothing is reclaimable.
            time.UtcNow = time.UtcNow.AddMinutes(10);
            (await sut.ClaimAsync("worker-2", CancellationToken.None)).Should().BeNull();

            // Past the lease the stale job is requeued and claimable by the next worker.
            time.UtcNow = time.UtcNow.AddMinutes(40);
            MixLabMapJob? reclaimed = await sut.ClaimAsync("worker-2", CancellationToken.None);

            reclaimed!.UploadId.Should().Be(created.UploadId);
            reclaimed.WorkerId.Should().Be("worker-2");
            reclaimed.Status.Should().Be(MixLabMapStatus.Running);
        }

        private static (ClaimMixLabMapUseCase Sut, BlobMixLabMapRepository Maps, FakeTimeProvider Time) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var maps = new BlobMixLabMapRepository(gateway, time, NullLogger<BlobMixLabMapRepository>.Instance);
            var sut = new ClaimMixLabMapUseCase(maps, new MixLabOptions { ClaimLeaseMinutes = 45 });
            return (sut, maps, time);
        }
    }
}
