using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
    public sealed class UploadCollectionUseCaseTests
    {
        [Test]
        public async Task UploadAsync_raw_xml_input_stores_gzipped_content_that_round_trips()
        {
            var repository = new SpyMixLabUploadRepository();
            var sut = new UploadCollectionUseCase(repository);
            byte[] raw = Encoding.UTF8.GetBytes("<DJ_PLAYLISTS>raw xml</DJ_PLAYLISTS>");

            await sut.UploadAsync(new MemoryStream(raw), contentEncodingSaysGzip: false, label: null, CancellationToken.None);

            Decompress(repository.SavedContent!).Should().Equal(raw);
        }

        [Test]
        public async Task UploadAsync_gzip_magic_bytes_input_is_stored_as_is()
        {
            var repository = new SpyMixLabUploadRepository();
            var sut = new UploadCollectionUseCase(repository);
            byte[] gzipped = Gzip(Encoding.UTF8.GetBytes("<DJ_PLAYLISTS>already gzipped</DJ_PLAYLISTS>"));

            await sut.UploadAsync(new MemoryStream(gzipped), contentEncodingSaysGzip: false, label: null, CancellationToken.None);

            repository.SavedContent.Should().Equal(gzipped);
        }

        [Test]
        public async Task UploadAsync_content_encoding_header_honoured_even_without_sniffable_head()
        {
            var repository = new SpyMixLabUploadRepository();
            var sut = new UploadCollectionUseCase(repository);

            // Not actually gzip-shaped bytes, but the caller asserts (via the header) that it is —
            // the use case must trust that signal rather than re-sniff and re-compress.
            byte[] notReallyGzipShaped = Encoding.UTF8.GetBytes("not-gzip-bytes");

            await sut.UploadAsync(new MemoryStream(notReallyGzipShaped), contentEncodingSaysGzip: true, label: null, CancellationToken.None);

            repository.SavedContent.Should().Equal(notReallyGzipShaped);
        }

        [Test]
        public async Task UploadAsync_passes_label_to_repository()
        {
            var repository = new SpyMixLabUploadRepository();
            var sut = new UploadCollectionUseCase(repository);

            await sut.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("xml")), contentEncodingSaysGzip: false, label: "my-label", CancellationToken.None);

            repository.SavedLabel.Should().Be("my-label");
        }

        [Test]
        public async Task UploadAsync_sizeBytes_reflects_stored_compressed_size()
        {
            var repository = new SpyMixLabUploadRepository();
            var sut = new UploadCollectionUseCase(repository);
            byte[] raw = Encoding.UTF8.GetBytes(new string('a', 5000));

            MixLabUpload upload = await sut.UploadAsync(new MemoryStream(raw), contentEncodingSaysGzip: false, label: null, CancellationToken.None);

            ((long)repository.SavedContent!.Length).Should().Be(repository.SavedSizeBytes);
            upload.SizeBytes.Should().Be(repository.SavedSizeBytes);
            upload.SizeBytes.Should().BeLessThan(raw.Length);
        }

        private static byte[] Gzip(byte[] raw)
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return compressed.ToArray();
        }

        private static byte[] Decompress(byte[] gzipped)
        {
            using var source = new MemoryStream(gzipped);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream();
            gzip.CopyTo(result);
            return result.ToArray();
        }

        private sealed class SpyMixLabUploadRepository : IMixLabUploadRepository
        {
            public byte[]? SavedContent { get; private set; }

            public long SavedSizeBytes { get; private set; }

            public string? SavedLabel { get; private set; }

            public Task<MixLabUpload> SaveAsync(Stream gzipContent, long sizeBytes, string? label, CancellationToken cancellationToken)
            {
                using var buffer = new MemoryStream();
                gzipContent.CopyTo(buffer);
                SavedContent = buffer.ToArray();
                SavedSizeBytes = sizeBytes;
                SavedLabel = label;

                return Task.FromResult(new MixLabUpload
                {
                    UploadId = "u_test",
                    UploadedAt = DateTimeOffset.UtcNow,
                    SizeBytes = sizeBytes,
                    Label = label,
                });
            }

            public Task<string?> GetLatestIdAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

            public Task<Stream> OpenReadAsync(string uploadId, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<MixLabUpload>> GetIndexAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<MixLabUpload>>(Array.Empty<MixLabUpload>());
        }
    }
}
