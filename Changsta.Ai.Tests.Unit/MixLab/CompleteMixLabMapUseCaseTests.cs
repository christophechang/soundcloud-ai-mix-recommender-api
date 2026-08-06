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
    public sealed class CompleteMixLabMapUseCaseTests
    {
        [Test]
        public async Task CompleteAsync_running_job_stores_payload_and_returns_true()
        {
            (CompleteMixLabMapUseCase sut, BlobMixLabMapRepository maps) = BuildSut();
            await maps.RequestAsync("u_1", CancellationToken.None);
            await maps.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            byte[] payload = new byte[] { 1, 2, 3 };
            bool completed = await sut.CompleteAsync("u_1", payload, CancellationToken.None);

            completed.Should().BeTrue();
            MixLabMapJob? job = await maps.GetJobAsync("u_1", CancellationToken.None);
            job!.Status.Should().Be(MixLabMapStatus.Succeeded);
            byte[]? stored = await maps.OpenPayloadAsync("u_1", CancellationToken.None);
            stored.Should().BeEquivalentTo(payload);
        }

        [Test]
        public async Task CompleteAsync_unknown_job_returns_false()
        {
            (CompleteMixLabMapUseCase sut, _) = BuildSut();

            bool completed = await sut.CompleteAsync("u_does_not_exist", new byte[] { 1 }, CancellationToken.None);

            completed.Should().BeFalse();
        }

        [Test]
        public async Task CompleteAsync_queued_but_unclaimed_job_returns_false()
        {
            (CompleteMixLabMapUseCase sut, BlobMixLabMapRepository maps) = BuildSut();
            await maps.RequestAsync("u_1", CancellationToken.None);

            bool completed = await sut.CompleteAsync("u_1", new byte[] { 1 }, CancellationToken.None);

            completed.Should().BeFalse();
        }

        [Test]
        public async Task CompleteAsync_already_succeeded_job_returns_false()
        {
            (CompleteMixLabMapUseCase sut, BlobMixLabMapRepository maps) = BuildSut();
            await maps.RequestAsync("u_1", CancellationToken.None);
            await maps.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await maps.CompleteAsync("u_1", new byte[] { 1 }, CancellationToken.None);

            bool completed = await sut.CompleteAsync("u_1", new byte[] { 2 }, CancellationToken.None);

            completed.Should().BeFalse();
        }

        [Test]
        public async Task FailAsync_running_job_stores_error_and_returns_true()
        {
            (FailMixLabMapUseCase sut, BlobMixLabMapRepository maps) = BuildFailSut();
            await maps.RequestAsync("u_1", CancellationToken.None);
            await maps.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            bool failed = await sut.FailAsync("u_1", "boom", CancellationToken.None);

            failed.Should().BeTrue();
            MixLabMapJob? job = await maps.GetJobAsync("u_1", CancellationToken.None);
            job!.Status.Should().Be(MixLabMapStatus.Failed);
            job.Error.Should().Be("boom");
        }

        [Test]
        public async Task FailAsync_queued_but_unclaimed_job_returns_false()
        {
            (FailMixLabMapUseCase sut, BlobMixLabMapRepository maps) = BuildFailSut();
            await maps.RequestAsync("u_1", CancellationToken.None);

            bool failed = await sut.FailAsync("u_1", "boom", CancellationToken.None);

            failed.Should().BeFalse();
        }

        [Test]
        public async Task FailAsync_unknown_job_returns_false()
        {
            (FailMixLabMapUseCase sut, _) = BuildFailSut();

            bool failed = await sut.FailAsync("u_does_not_exist", "boom", CancellationToken.None);

            failed.Should().BeFalse();
        }

        private static (CompleteMixLabMapUseCase Sut, BlobMixLabMapRepository Maps) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var maps = new BlobMixLabMapRepository(gateway, time, NullLogger<BlobMixLabMapRepository>.Instance);
            var sut = new CompleteMixLabMapUseCase(maps);
            return (sut, maps);
        }

        private static (FailMixLabMapUseCase Sut, BlobMixLabMapRepository Maps) BuildFailSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var maps = new BlobMixLabMapRepository(gateway, time, NullLogger<BlobMixLabMapRepository>.Instance);
            var sut = new FailMixLabMapUseCase(maps);
            return (sut, maps);
        }
    }
}
