using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    public sealed class OpenUploadUseCaseTests
    {
        [Test]
        public async Task OpenAsync_latest_resolves_via_repository_latest_id()
        {
            var repository = new FakeMixLabUploadRepository();
            repository.Seed("u_1", Encoding.UTF8.GetBytes("one"));
            repository.Seed("u_2", Encoding.UTF8.GetBytes("two"));
            repository.LatestId = "u_2";
            var sut = new OpenUploadUseCase(repository);

            MixLabUploadContent? content = await sut.OpenAsync("latest", CancellationToken.None);

            content.Should().NotBeNull();
            content!.UploadId.Should().Be("u_2");
            ReadAll(content.Content).Should().Equal(Encoding.UTF8.GetBytes("two"));
        }

        [Test]
        public async Task OpenAsync_latest_returns_null_when_no_uploads_exist()
        {
            var repository = new FakeMixLabUploadRepository();
            var sut = new OpenUploadUseCase(repository);

            MixLabUploadContent? content = await sut.OpenAsync("latest", CancellationToken.None);

            content.Should().BeNull();
        }

        [Test]
        public async Task OpenAsync_known_id_opens_the_matching_stream()
        {
            var repository = new FakeMixLabUploadRepository();
            repository.Seed("u_1", Encoding.UTF8.GetBytes("one"));
            var sut = new OpenUploadUseCase(repository);

            MixLabUploadContent? content = await sut.OpenAsync("u_1", CancellationToken.None);

            content.Should().NotBeNull();
            content!.UploadId.Should().Be("u_1");
        }

        [Test]
        public async Task OpenAsync_unknown_id_returns_null_and_does_not_open_a_stream()
        {
            var repository = new FakeMixLabUploadRepository();
            repository.Seed("u_1", Encoding.UTF8.GetBytes("one"));
            var sut = new OpenUploadUseCase(repository);

            MixLabUploadContent? content = await sut.OpenAsync("u_unknown", CancellationToken.None);

            content.Should().BeNull();
            repository.OpenReadCallCount.Should().Be(0);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private sealed class FakeMixLabUploadRepository : IMixLabUploadRepository
        {
            private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
            private readonly List<MixLabUpload> _index = new();

            public string? LatestId { get; set; }

            public int OpenReadCallCount { get; private set; }

            public void Seed(string uploadId, byte[] content)
            {
                _blobs[uploadId] = content;
                _index.Add(new MixLabUpload { UploadId = uploadId, UploadedAt = DateTimeOffset.UtcNow, SizeBytes = content.Length });
            }

            public Task<MixLabUpload> SaveAsync(Stream gzipContent, long sizeBytes, string? label, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<string?> GetLatestIdAsync(CancellationToken cancellationToken) => Task.FromResult(LatestId);

            public Task<Stream> OpenReadAsync(string uploadId, CancellationToken cancellationToken)
            {
                OpenReadCallCount++;
                return Task.FromResult<Stream>(new MemoryStream(_blobs[uploadId]));
            }

            public Task<IReadOnlyList<MixLabUpload>> GetIndexAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<MixLabUpload>>(_index.ToArray());
        }
    }
}
