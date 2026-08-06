using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    internal sealed class BlobMixLabMapRepositoryTests
    {
        [Test]
        public async Task RequestAsync_new_upload_creates_queued_entry()
        {
            (BlobMixLabMapRepository sut, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            MixLabMapJob job = await sut.RequestAsync("u_1", CancellationToken.None);

            job.UploadId.Should().Be("u_1");
            job.Status.Should().Be(MixLabMapStatus.Queued);
            job.RequestedAt.Should().Be(time.UtcNow);
            job.ClaimedAt.Should().BeNull();
            job.WorkerId.Should().BeNull();
            job.CompletedAt.Should().BeNull();
            job.Error.Should().BeNull();
        }

        [Test]
        public async Task RequestAsync_queued_entry_is_idempotent()
        {
            (BlobMixLabMapRepository sut, FakeMixLabBlobGateway gateway, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            MixLabMapJob first = await sut.RequestAsync("u_1", CancellationToken.None);
            int writesAfterFirst = gateway.WrittenPaths.Count;

            time.UtcNow = time.UtcNow.AddMinutes(5);
            MixLabMapJob second = await sut.RequestAsync("u_1", CancellationToken.None);

            second.Should().Be(first);
            second.RequestedAt.Should().Be(first.RequestedAt);
            gateway.WrittenPaths.Count.Should().Be(writesAfterFirst);
        }

        [Test]
        public async Task RequestAsync_succeeded_entry_requeues_for_refresh()
        {
            (BlobMixLabMapRepository sut, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            await sut.RequestAsync("u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await sut.CompleteAsync("u_1", new byte[] { 1, 2, 3 }, CancellationToken.None);

            time.UtcNow = time.UtcNow.AddMinutes(10);
            MixLabMapJob refreshed = await sut.RequestAsync("u_1", CancellationToken.None);

            refreshed.Status.Should().Be(MixLabMapStatus.Queued);
            refreshed.RequestedAt.Should().Be(time.UtcNow);
            refreshed.ClaimedAt.Should().BeNull();
            refreshed.WorkerId.Should().BeNull();
            refreshed.CompletedAt.Should().BeNull();
            refreshed.Error.Should().BeNull();
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_claims_oldest_by_requestedAt_and_marks_running()
        {
            (BlobMixLabMapRepository sut, _, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            MixLabMapJob older = await sut.RequestAsync("u_1", CancellationToken.None);

            time.UtcNow = time.UtcNow.AddMinutes(5);
            await sut.RequestAsync("u_2", CancellationToken.None);

            MixLabMapJob? claimed = await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().NotBeNull();
            claimed!.UploadId.Should().Be(older.UploadId);
            claimed.Status.Should().Be(MixLabMapStatus.Running);
            claimed.WorkerId.Should().Be("worker-1");
            claimed.ClaimedAt.Should().Be(time.UtcNow);
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_requeues_stale_running_before_claiming()
        {
            (BlobMixLabMapRepository sut, _, FakeTimeProvider time) = BuildSut();

            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
            await sut.RequestAsync("u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            // Past the 45-minute stale-claim lease.
            time.UtcNow = time.UtcNow.AddMinutes(46);

            MixLabMapJob? reclaimed = await sut.TryClaimOldestQueuedAsync("worker-2", TimeSpan.FromMinutes(45), CancellationToken.None);

            reclaimed.Should().NotBeNull();
            reclaimed!.UploadId.Should().Be("u_1");
            reclaimed.WorkerId.Should().Be("worker-2");
            reclaimed.Status.Should().Be(MixLabMapStatus.Running);
            reclaimed.ClaimedAt.Should().Be(time.UtcNow);
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_returns_null_when_nothing_queued()
        {
            (BlobMixLabMapRepository sut, _, _) = BuildSut();

            MixLabMapJob? claimed = await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().BeNull();
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_empty_index_does_not_write()
        {
            (BlobMixLabMapRepository sut, FakeMixLabBlobGateway gateway, _) = BuildSut();

            MixLabMapJob? claimed = await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().BeNull();
            gateway.WrittenPaths.Should().BeEmpty();
        }

        [Test]
        public async Task TryClaimOldestQueuedAsync_no_actionable_entries_does_not_write()
        {
            (BlobMixLabMapRepository sut, FakeMixLabBlobGateway gateway, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            await sut.RequestAsync("u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            await sut.CompleteAsync("u_1", new byte[] { 1 }, CancellationToken.None);

            await sut.RequestAsync("u_2", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-2", TimeSpan.FromMinutes(45), CancellationToken.None);
            await sut.FailAsync("u_2", "boom", CancellationToken.None);

            await sut.RequestAsync("u_3", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-3", TimeSpan.FromMinutes(45), CancellationToken.None);

            // u_3 is Running but well within the lease — nothing here is stale or queued.
            time.UtcNow = time.UtcNow.AddMinutes(5);

            int writesBefore = gateway.WrittenPaths.Count;

            MixLabMapJob? claimed = await sut.TryClaimOldestQueuedAsync("worker-4", TimeSpan.FromMinutes(45), CancellationToken.None);

            claimed.Should().BeNull();
            gateway.WrittenPaths.Count.Should().Be(writesBefore);
        }

        [Test]
        public async Task CompleteAsync_stores_payload_verbatim_and_marks_succeeded()
        {
            (BlobMixLabMapRepository sut, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            await sut.RequestAsync("u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            byte[] payload = Encoding.UTF8.GetBytes("{\"nodes\":[]}");
            time.UtcNow = time.UtcNow.AddMinutes(2);
            await sut.CompleteAsync("u_1", payload, CancellationToken.None);

            byte[]? stored = await sut.OpenPayloadAsync("u_1", CancellationToken.None);
            stored.Should().BeEquivalentTo(payload);

            MixLabMapJob? job = await sut.GetJobAsync("u_1", CancellationToken.None);
            job!.Status.Should().Be(MixLabMapStatus.Succeeded);
            job.CompletedAt.Should().Be(time.UtcNow);
            job.Error.Should().BeNull();
        }

        [Test]
        public async Task CompleteAsync_survives_index_write_conflict()
        {
            (BlobMixLabMapRepository sut, FakeMixLabBlobGateway gateway, _) = BuildSut();

            await sut.RequestAsync("u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            // Only one forced conflict: unconditional payload write must not consume it, so it must
            // land on the index write's retry loop and still succeed within the retry budget.
            gateway.ForcedConflictsRemaining = 1;

            byte[] payload = new byte[] { 9, 8, 7 };
            Func<Task> act = () => sut.CompleteAsync("u_1", payload, CancellationToken.None);

            await act.Should().NotThrowAsync();

            MixLabMapJob? job = await sut.GetJobAsync("u_1", CancellationToken.None);
            job!.Status.Should().Be(MixLabMapStatus.Succeeded);

            byte[]? stored = await sut.OpenPayloadAsync("u_1", CancellationToken.None);
            stored.Should().BeEquivalentTo(payload);
        }

        [Test]
        public async Task FailAsync_marks_failed_with_error()
        {
            (BlobMixLabMapRepository sut, _, FakeTimeProvider time) = BuildSut();
            time.UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

            await sut.RequestAsync("u_1", CancellationToken.None);
            await sut.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            time.UtcNow = time.UtcNow.AddMinutes(3);
            await sut.FailAsync("u_1", "boom", CancellationToken.None);

            MixLabMapJob? job = await sut.GetJobAsync("u_1", CancellationToken.None);
            job!.Status.Should().Be(MixLabMapStatus.Failed);
            job.Error.Should().Be("boom");
            job.CompletedAt.Should().Be(time.UtcNow);
        }

        [Test]
        public async Task GetJobAsync_and_OpenPayloadAsync_return_null_when_absent()
        {
            (BlobMixLabMapRepository sut, _, _) = BuildSut();

            (await sut.GetJobAsync("does-not-exist", CancellationToken.None)).Should().BeNull();
            (await sut.OpenPayloadAsync("does-not-exist", CancellationToken.None)).Should().BeNull();
        }

        private static (BlobMixLabMapRepository Sut, FakeMixLabBlobGateway Gateway, FakeTimeProvider Time) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var sut = new BlobMixLabMapRepository(gateway, time, NullLogger<BlobMixLabMapRepository>.Instance);
            return (sut, gateway, time);
        }
    }
}
