using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class CataloguePersisterTests
    {
        [Test]
        public async Task Does_not_write_when_the_blob_read_failed()
        {
            var repository = new RecordingRepository();
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            // A failed blob read leaves an RSS-only catalogue; persisting it would destroy data.
            await persister.PersistIfChangedAsync(
                new[] { MakeMix("1") },
                MakeLoad(blobReadSucceeded: false, blobMixes: Array.Empty<Mix>(), rssMixes: new[] { MakeMix("1") }),
                derivedFieldsChanged: true,
                CancellationToken.None);

            repository.WriteCount.Should().Be(0);
        }

        [Test]
        public async Task Does_not_write_when_nothing_changed()
        {
            var repository = new RecordingRepository();
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            Mix[] same = { MakeMix("1") };

            await persister.PersistIfChangedAsync(
                same,
                MakeLoad(blobReadSucceeded: true, blobMixes: same, rssMixes: same),
                derivedFieldsChanged: false,
                CancellationToken.None);

            repository.WriteCount.Should().Be(0);
        }

        [Test]
        public async Task Writes_when_a_derived_field_changed_even_with_no_new_mixes()
        {
            var repository = new RecordingRepository();
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            Mix[] same = { MakeMix("1") };

            await persister.PersistIfChangedAsync(
                same,
                MakeLoad(blobReadSucceeded: true, blobMixes: same, rssMixes: same),
                derivedFieldsChanged: true,
                CancellationToken.None);

            repository.WriteCount.Should().Be(1);
        }

        [Test]
        public async Task Writes_when_rss_brought_a_new_mix()
        {
            var repository = new RecordingRepository();
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            Mix[] merged = { MakeMix("1"), MakeMix("2") };

            await persister.PersistIfChangedAsync(
                merged,
                MakeLoad(
                    blobReadSucceeded: true,
                    blobMixes: new[] { MakeMix("1") },
                    rssMixes: new[] { MakeMix("2") }),
                derivedFieldsChanged: false,
                CancellationToken.None);

            repository.WriteCount.Should().Be(1);
        }

        [Test]
        public async Task Writes_when_genre_normalisation_changed_the_blob()
        {
            var repository = new RecordingRepository();
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            Mix[] same = { MakeMix("1") };

            await persister.PersistIfChangedAsync(
                same,
                MakeLoad(blobReadSucceeded: true, blobMixes: same, rssMixes: same, blobGenresChanged: true),
                derivedFieldsChanged: false,
                CancellationToken.None);

            repository.WriteCount.Should().Be(1);
        }

        [Test]
        public async Task Passes_the_etag_through_for_optimistic_concurrency()
        {
            var repository = new RecordingRepository();
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            Mix[] same = { MakeMix("1") };

            await persister.PersistIfChangedAsync(
                same,
                MakeLoad(blobReadSucceeded: true, blobMixes: same, rssMixes: same, etag: "etag-99"),
                derivedFieldsChanged: true,
                CancellationToken.None);

            repository.LastETag.Should().Be("etag-99");
        }

        [Test]
        public async Task A_concurrent_write_does_not_fail_the_load()
        {
            var repository = new RecordingRepository(
                new CatalogConcurrencyException("blob changed under us"));
            var persister = new CataloguePersister(repository, NullLogger.Instance);

            Mix[] same = { MakeMix("1") };

            Func<Task> act = () => persister.PersistIfChangedAsync(
                same,
                MakeLoad(blobReadSucceeded: true, blobMixes: same, rssMixes: same),
                derivedFieldsChanged: true,
                CancellationToken.None);

            // The in-memory catalogue is still served; persistence retries on the next load.
            await act.Should().NotThrowAsync();
        }

        private static CatalogueLoadResult MakeLoad(
            bool blobReadSucceeded,
            IReadOnlyList<Mix> blobMixes,
            IReadOnlyList<Mix> rssMixes,
            bool blobGenresChanged = false,
            string? etag = null) => new CatalogueLoadResult
            {
                BlobMixes = blobMixes,
                RssMixes = rssMixes,
                BlobETag = etag,
                BlobReadSucceeded = blobReadSucceeded,
                BlobGenresChanged = blobGenresChanged,
            };

        private static Mix MakeMix(string id) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://soundcloud.com/changsta/{id}",
            Genre = "dnb",
            Energy = "mid",
            Tracklist = Array.Empty<Track>(),
        };

        private sealed class RecordingRepository : IBlobMixCatalogueRepository
        {
            private readonly Exception? _writeException;

            public RecordingRepository(Exception? writeException = null) => _writeException = writeException;

            public int WriteCount { get; private set; }

            public string? LastETag { get; private set; }

            public Task<CatalogReadResult> ReadAsync(CancellationToken cancellationToken) =>
                Task.FromResult(new CatalogReadResult(Array.Empty<Mix>(), null));

            public Task WriteAsync(IReadOnlyList<Mix> mixes, string? expectedETag, CancellationToken cancellationToken)
            {
                WriteCount++;
                LastETag = expectedETag;

                if (_writeException is not null)
                {
                    throw _writeException;
                }

                return Task.CompletedTask;
            }
        }
    }
}
