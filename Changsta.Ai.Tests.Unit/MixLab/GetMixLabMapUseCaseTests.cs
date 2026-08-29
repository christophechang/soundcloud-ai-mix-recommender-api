using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class GetMixLabMapUseCaseTests
    {
        [Test]
        public async Task GetAsync_succeeded_job_returns_found_with_payload()
        {
            (GetMixLabMapUseCase sut, BlobMixLabMapRepository maps, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");
            await maps.RequestAsync("u_1", CancellationToken.None);
            await maps.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);
            byte[] payload = new byte[] { 1, 2, 3 };
            await maps.CompleteAsync("u_1", payload, CancellationToken.None);

            GetMixLabMapResult result = await sut.GetAsync("u_1", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.Found);
            result.Job.Should().NotBeNull();
            result.Job!.Status.Should().Be(MixLabMapStatus.Succeeded);
            result.Payload.Should().BeEquivalentTo(payload);
        }

        [Test]
        public async Task GetAsync_queued_job_returns_found_without_payload()
        {
            (GetMixLabMapUseCase sut, BlobMixLabMapRepository maps, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");
            await maps.RequestAsync("u_1", CancellationToken.None);

            GetMixLabMapResult result = await sut.GetAsync("u_1", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.Found);
            result.Job!.Status.Should().Be(MixLabMapStatus.Queued);
            result.Payload.Should().BeNull();
        }

        [Test]
        public async Task GetAsync_latest_resolves_to_newest_upload_job()
        {
            (GetMixLabMapUseCase sut, BlobMixLabMapRepository maps, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");
            uploads.Seed("u_2");
            uploads.Latest = "u_2";
            await maps.RequestAsync("u_1", CancellationToken.None);
            await maps.RequestAsync("u_2", CancellationToken.None);

            GetMixLabMapResult result = await sut.GetAsync("latest", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.Found);
            result.Job!.UploadId.Should().Be("u_2");
        }

        [Test]
        public async Task GetAsync_latest_with_no_uploads_is_not_found()
        {
            (GetMixLabMapUseCase sut, _, _) = BuildSut();

            GetMixLabMapResult result = await sut.GetAsync("latest", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.NotFound);
        }

        [Test]
        public async Task GetAsync_unknown_concrete_upload_with_no_map_job_is_not_found()
        {
            (GetMixLabMapUseCase sut, _, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");

            GetMixLabMapResult result = await sut.GetAsync("u_does_not_exist", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.NotFound);
        }

        [Test]
        public async Task GetAsync_concrete_upload_with_map_job_returns_found_even_when_upload_was_pruned()
        {
            (GetMixLabMapUseCase sut, BlobMixLabMapRepository maps, _) = BuildSut();
            await maps.RequestAsync("u_pruned", CancellationToken.None);

            GetMixLabMapResult result = await sut.GetAsync("u_pruned", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.Found);
            result.Job!.UploadId.Should().Be("u_pruned");
        }

        [Test]
        public async Task GetAsync_known_upload_with_no_map_job_is_not_found()
        {
            (GetMixLabMapUseCase sut, _, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");

            GetMixLabMapResult result = await sut.GetAsync("u_1", CancellationToken.None);

            result.Outcome.Should().Be(GetMixLabMapResult.GetOutcome.NotFound);
        }

        private static (GetMixLabMapUseCase Sut, BlobMixLabMapRepository Maps, StubMixLabUploadRepository Uploads) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var maps = new BlobMixLabMapRepository(gateway, time, NullLogger<BlobMixLabMapRepository>.Instance);
            var uploads = new StubMixLabUploadRepository();
            var sut = new GetMixLabMapUseCase(maps, uploads);
            return (sut, maps, uploads);
        }

        private sealed class StubMixLabUploadRepository : IMixLabUploadRepository
        {
            private readonly List<MixLabUpload> _uploads = new();

            public string? Latest { get; set; }

            public void Seed(string uploadId)
            {
                _uploads.Add(new MixLabUpload
                {
                    UploadId = uploadId,
                    UploadedAt = DateTimeOffset.UtcNow,
                    SizeBytes = 1,
                });
                Latest = uploadId;
            }

            public Task<MixLabUpload> SaveAsync(Stream gzipContent, long sizeBytes, string? label, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string?> GetLatestIdAsync(CancellationToken cancellationToken) => Task.FromResult(Latest);

            public Task<Stream> OpenReadAsync(string uploadId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<MixLabUpload>> GetIndexAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<MixLabUpload>>(_uploads);
        }
    }
}
