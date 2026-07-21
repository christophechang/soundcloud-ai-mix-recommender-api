using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class CatalogueLoaderTests
    {
        [Test]
        public async Task Returns_both_sources_and_the_etag()
        {
            var loader = new CatalogueLoader(
                new StubRssProvider(new[] { MakeMix("rss-1") }),
                new StubRepository(new[] { MakeMix("blob-1") }, "etag-abc"),
                NullLogger.Instance);

            CatalogueLoadResult result = await loader.LoadAsync(CancellationToken.None);

            result.BlobMixes.Select(m => m.Id).Should().Equal("blob-1");
            result.RssMixes.Select(m => m.Id).Should().Equal("rss-1");
            result.BlobETag.Should().Be("etag-abc");
            result.BlobReadSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task Blob_read_failure_degrades_to_rss_only_and_blocks_write_back()
        {
            var loader = new CatalogueLoader(
                new StubRssProvider(new[] { MakeMix("rss-1") }),
                new ThrowingRepository(),
                NullLogger.Instance);

            CatalogueLoadResult result = await loader.LoadAsync(CancellationToken.None);

            result.BlobMixes.Should().BeEmpty();
            result.RssMixes.Select(m => m.Id).Should().Equal("rss-1");

            // The critical invariant: an RSS-only catalogue must never overwrite intact blob data.
            result.BlobReadSucceeded.Should().BeFalse();
            result.BlobETag.Should().BeNull();
        }

        [Test]
        public async Task Rss_failure_degrades_to_blob_only_and_still_allows_write_back()
        {
            var loader = new CatalogueLoader(
                new ThrowingRssProvider(),
                new StubRepository(new[] { MakeMix("blob-1") }, "etag-abc"),
                NullLogger.Instance);

            CatalogueLoadResult result = await loader.LoadAsync(CancellationToken.None);

            result.RssMixes.Should().BeEmpty();
            result.BlobMixes.Select(m => m.Id).Should().Equal("blob-1");
            result.BlobReadSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task Reports_when_genre_normalisation_rewrote_a_blob_mix()
        {
            var loader = new CatalogueLoader(
                new StubRssProvider(Array.Empty<Mix>()),
                new StubRepository(new[] { MakeMix("blob-1", genre: "Drum & Bass") }, "etag"),
                NullLogger.Instance);

            CatalogueLoadResult result = await loader.LoadAsync(CancellationToken.None);

            result.BlobGenresChanged.Should().BeTrue();
            result.BlobMixes[0].Genre.Should().Be("dnb");
        }

        [Test]
        public async Task Reports_no_change_when_genres_are_already_normalised()
        {
            var loader = new CatalogueLoader(
                new StubRssProvider(Array.Empty<Mix>()),
                new StubRepository(new[] { MakeMix("blob-1", genre: "dnb") }, "etag"),
                NullLogger.Instance);

            CatalogueLoadResult result = await loader.LoadAsync(CancellationToken.None);

            result.BlobGenresChanged.Should().BeFalse();
        }

        [Test]
        public void Cancellation_is_not_swallowed()
        {
            var loader = new CatalogueLoader(
                new StubRssProvider(Array.Empty<Mix>()),
                new ThrowingRepository(new OperationCanceledException()),
                NullLogger.Instance);

            Assert.ThrowsAsync<OperationCanceledException>(
                () => loader.LoadAsync(CancellationToken.None));
        }

        private static Mix MakeMix(string id, string genre = "dnb") => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://soundcloud.com/changsta/{id}",
            Genre = genre,
            Energy = "mid",
            Tracklist = Array.Empty<Track>(),
        };

        private sealed class StubRssProvider : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;

            public StubRssProvider(IReadOnlyList<Mix> mixes) => _mixes = mixes;

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken) =>
                Task.FromResult(_mixes);
        }

        private sealed class ThrowingRssProvider : IMixCatalogueProvider
        {
            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("rss down");
        }

        private sealed class StubRepository : IBlobMixCatalogueRepository
        {
            private readonly IReadOnlyList<Mix> _mixes;
            private readonly string? _etag;

            public StubRepository(IReadOnlyList<Mix> mixes, string? etag)
            {
                _mixes = mixes;
                _etag = etag;
            }

            public Task<CatalogReadResult> ReadAsync(CancellationToken cancellationToken) =>
                Task.FromResult(new CatalogReadResult(_mixes, _etag));

            public Task WriteAsync(IReadOnlyList<Mix> mixes, string? expectedETag, CancellationToken cancellationToken) =>
                Task.CompletedTask;
        }

        private sealed class ThrowingRepository : IBlobMixCatalogueRepository
        {
            private readonly Exception _exception;

            public ThrowingRepository(Exception? exception = null) =>
                _exception = exception ?? new InvalidOperationException("blob down");

            public Task<CatalogReadResult> ReadAsync(CancellationToken cancellationToken) =>
                throw _exception;

            public Task WriteAsync(IReadOnlyList<Mix> mixes, string? expectedETag, CancellationToken cancellationToken) =>
                Task.CompletedTask;
        }
    }
}
