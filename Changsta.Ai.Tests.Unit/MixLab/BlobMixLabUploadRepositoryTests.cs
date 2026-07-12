using System;
using System.IO;
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
    public sealed class BlobMixLabUploadRepositoryTests
    {
        [Test]
        public async Task SaveAsync_beyond_five_uploads_prunes_oldest_and_deletes_its_blob()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider { UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero) };
            var sut = new BlobMixLabUploadRepository(gateway, time, NullLogger<BlobMixLabUploadRepository>.Instance);

            MixLabUpload first = await SaveOneAsync(sut);
            for (int i = 0; i < 5; i++)
            {
                time.UtcNow = time.UtcNow.AddMinutes(1);
                await SaveOneAsync(sut);
            }

            var index = await sut.GetIndexAsync(CancellationToken.None);

            index.Should().HaveCount(5);
            index.Should().NotContain(u => u.UploadId == first.UploadId);
            gateway.DeletedPaths.Should().ContainSingle(p => p.Contains(first.UploadId, StringComparison.Ordinal));
        }

        [Test]
        public async Task GetLatestIdAsync_returns_most_recently_saved_upload()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider { UtcNow = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero) };
            var sut = new BlobMixLabUploadRepository(gateway, time, NullLogger<BlobMixLabUploadRepository>.Instance);

            await SaveOneAsync(sut);
            time.UtcNow = time.UtcNow.AddMinutes(1);
            MixLabUpload latest = await SaveOneAsync(sut);

            string? latestId = await sut.GetLatestIdAsync(CancellationToken.None);

            latestId.Should().Be(latest.UploadId);
        }

        [Test]
        public async Task GetLatestIdAsync_no_uploads_returns_null()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var sut = new BlobMixLabUploadRepository(gateway, time, NullLogger<BlobMixLabUploadRepository>.Instance);

            string? latestId = await sut.GetLatestIdAsync(CancellationToken.None);

            latestId.Should().BeNull();
        }

        [Test]
        public async Task SaveAsync_then_OpenReadAsync_round_trips_content()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var sut = new BlobMixLabUploadRepository(gateway, time, NullLogger<BlobMixLabUploadRepository>.Instance);

            byte[] payload = Encoding.UTF8.GetBytes("gzip-bytes");
            MixLabUpload upload = await sut.SaveAsync(new MemoryStream(payload), payload.Length, "collection", CancellationToken.None);

            await using Stream read = await sut.OpenReadAsync(upload.UploadId, CancellationToken.None);
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer, CancellationToken.None);

            buffer.ToArray().Should().Equal(payload);
        }

        private static async Task<MixLabUpload> SaveOneAsync(BlobMixLabUploadRepository sut)
        {
            byte[] payload = Encoding.UTF8.GetBytes("gzip-" + Guid.NewGuid());
            return await sut.SaveAsync(new MemoryStream(payload), payload.Length, label: null, CancellationToken.None);
        }
    }
}
