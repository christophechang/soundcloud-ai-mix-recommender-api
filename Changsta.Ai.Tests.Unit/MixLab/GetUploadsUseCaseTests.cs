using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class GetUploadsUseCaseTests
    {
        [Test]
        public async Task GetUploadsAsync_returns_repository_index_verbatim()
        {
            var uploads = new[]
            {
                new MixLabUpload { UploadId = "u_1", UploadedAt = DateTimeOffset.UtcNow, SizeBytes = 10 },
                new MixLabUpload { UploadId = "u_2", UploadedAt = DateTimeOffset.UtcNow, SizeBytes = 20 },
            };
            var repository = new StubMixLabUploadRepository(uploads);
            var sut = new GetUploadsUseCase(repository);

            IReadOnlyList<MixLabUpload> result = await sut.GetUploadsAsync(CancellationToken.None);

            result.Should().Equal(uploads);
        }

        private sealed class StubMixLabUploadRepository : IMixLabUploadRepository
        {
            private readonly IReadOnlyList<MixLabUpload> _uploads;

            public StubMixLabUploadRepository(IReadOnlyList<MixLabUpload> uploads)
            {
                _uploads = uploads;
            }

            public Task<MixLabUpload> SaveAsync(Stream gzipContent, long sizeBytes, string? label, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string?> GetLatestIdAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

            public Task<Stream> OpenReadAsync(string uploadId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<MixLabUpload>> GetIndexAsync(CancellationToken cancellationToken) => Task.FromResult(_uploads);
        }
    }
}
