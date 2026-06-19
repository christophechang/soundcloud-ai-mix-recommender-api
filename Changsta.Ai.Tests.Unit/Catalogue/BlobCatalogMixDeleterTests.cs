using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class BlobCatalogMixDeleterTests
    {
        [Test]
        public async Task DeleteBySlugAsync_writes_remaining_mixes_with_read_etag()
        {
            Mix target = MakeMix("https://soundcloud.test/artist/keep-1");
            Mix other = MakeMix("https://soundcloud.test/artist/keep-2");
            var repo = new FakeRepo { Mixes = { target, other } };
            var sut = new BlobCatalogMixDeleter(repo, NullLogger<BlobCatalogMixDeleter>.Instance);

            bool deleted = await sut.DeleteBySlugAsync(SlugOf(target), CancellationToken.None);

            deleted.Should().BeTrue();
            repo.WriteCallCount.Should().Be(1);
            repo.LastWritten.Should().ContainSingle().Which.Url.Should().Be(other.Url);
            repo.LastExpectedETag.Should().Be("etag-1");
        }

        [Test]
        public async Task DeleteBySlugAsync_returns_false_when_slug_not_found()
        {
            var repo = new FakeRepo { Mixes = { MakeMix("https://soundcloud.test/artist/keep-1") } };
            var sut = new BlobCatalogMixDeleter(repo, NullLogger<BlobCatalogMixDeleter>.Instance);

            bool deleted = await sut.DeleteBySlugAsync("does-not-exist", CancellationToken.None);

            deleted.Should().BeFalse();
            repo.WriteCallCount.Should().Be(0);
        }

        [Test]
        public async Task DeleteBySlugAsync_re_reads_and_retries_on_write_conflict()
        {
            Mix target = MakeMix("https://soundcloud.test/artist/keep-1");
            var repo = new FakeRepo { Mixes = { target }, ConcurrencyFailuresBeforeSuccess = 2 };
            var sut = new BlobCatalogMixDeleter(repo, NullLogger<BlobCatalogMixDeleter>.Instance);

            bool deleted = await sut.DeleteBySlugAsync(SlugOf(target), CancellationToken.None);

            deleted.Should().BeTrue();
            repo.WriteCallCount.Should().Be(3);
            repo.ReadCallCount.Should().Be(3);
        }

        [Test]
        public async Task DeleteBySlugAsync_throws_after_exhausting_retries()
        {
            Mix target = MakeMix("https://soundcloud.test/artist/keep-1");
            var repo = new FakeRepo { Mixes = { target }, AlwaysConflict = true };
            var sut = new BlobCatalogMixDeleter(repo, NullLogger<BlobCatalogMixDeleter>.Instance);

            Func<Task> act = () => sut.DeleteBySlugAsync(SlugOf(target), CancellationToken.None);

            await act.Should().ThrowAsync<CatalogConcurrencyException>();
            repo.WriteCallCount.Should().Be(3);
        }

        private static Mix MakeMix(string url) => new Mix
        {
            Id = url,
            Title = "Title",
            Url = url,
            Genre = "house",
            Energy = "mid",
        };

        private static string SlugOf(Mix mix) => MixSlugHelper.ExtractSlug(mix.Url);

        private sealed class FakeRepo : IBlobMixCatalogueRepository
        {
            public List<Mix> Mixes { get; } = new();

            public int ConcurrencyFailuresBeforeSuccess { get; set; }

            public bool AlwaysConflict { get; set; }

            public int WriteCallCount { get; private set; }

            public int ReadCallCount { get; private set; }

            public IReadOnlyList<Mix>? LastWritten { get; private set; }

            public string? LastExpectedETag { get; private set; }

            public Task<CatalogReadResult> ReadAsync(CancellationToken cancellationToken)
            {
                ReadCallCount++;
                return Task.FromResult(new CatalogReadResult(Mixes.ToArray(), $"etag-{ReadCallCount}"));
            }

            public Task WriteAsync(IReadOnlyList<Mix> mixes, string? expectedETag, CancellationToken cancellationToken)
            {
                WriteCallCount++;
                if (AlwaysConflict || WriteCallCount <= ConcurrencyFailuresBeforeSuccess)
                {
                    throw new CatalogConcurrencyException("simulated write conflict");
                }

                LastWritten = mixes;
                LastExpectedETag = expectedETag;
                return Task.CompletedTask;
            }
        }
    }
}
