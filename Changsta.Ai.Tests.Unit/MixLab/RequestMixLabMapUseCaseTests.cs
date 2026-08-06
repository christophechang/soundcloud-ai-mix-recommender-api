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
    public sealed class RequestMixLabMapUseCaseTests
    {
        [Test]
        public async Task RequestAsync_concrete_known_upload_requests_a_queued_job()
        {
            (RequestMixLabMapUseCase sut, BlobMixLabMapRepository maps, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");

            RequestMixLabMapResult result = await sut.RequestAsync("u_1", CancellationToken.None);

            result.Outcome.Should().Be(RequestMixLabMapResult.RequestOutcome.Accepted);
            result.Job.Should().NotBeNull();
            result.Job!.UploadId.Should().Be("u_1");
            result.Job.Status.Should().Be(MixLabMapStatus.Queued);

            MixLabMapJob? stored = await maps.GetJobAsync("u_1", CancellationToken.None);
            stored.Should().NotBeNull();
        }

        [Test]
        public async Task RequestAsync_latest_resolves_to_newest_upload()
        {
            (RequestMixLabMapUseCase sut, _, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");
            uploads.Seed("u_2");
            uploads.Latest = "u_2";

            RequestMixLabMapResult result = await sut.RequestAsync("latest", CancellationToken.None);

            result.Outcome.Should().Be(RequestMixLabMapResult.RequestOutcome.Accepted);
            result.Job!.UploadId.Should().Be("u_2");
        }

        [Test]
        public async Task RequestAsync_latest_with_no_uploads_is_not_found()
        {
            (RequestMixLabMapUseCase sut, _, _) = BuildSut();

            RequestMixLabMapResult result = await sut.RequestAsync("latest", CancellationToken.None);

            result.Outcome.Should().Be(RequestMixLabMapResult.RequestOutcome.NoUploadsAvailable);
            result.Job.Should().BeNull();
        }

        [Test]
        public async Task RequestAsync_unknown_concrete_upload_is_not_found()
        {
            (RequestMixLabMapUseCase sut, _, StubMixLabUploadRepository uploads) = BuildSut();
            uploads.Seed("u_1");

            RequestMixLabMapResult result = await sut.RequestAsync("u_does_not_exist", CancellationToken.None);

            result.Outcome.Should().Be(RequestMixLabMapResult.RequestOutcome.UnknownUpload);
            result.Job.Should().BeNull();
        }

        private static (RequestMixLabMapUseCase Sut, BlobMixLabMapRepository Maps, StubMixLabUploadRepository Uploads) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var maps = new BlobMixLabMapRepository(gateway, time, NullLogger<BlobMixLabMapRepository>.Instance);
            var uploads = new StubMixLabUploadRepository();
            var sut = new RequestMixLabMapUseCase(maps, uploads);
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
