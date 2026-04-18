using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class BlobBackedMixCatalogueProviderTests
    {
        [Test]
        public async Task GetLatestAsync_merges_blob_and_rss_rss_wins_on_url_collision()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1", "Old Title");
            var rssMix = MakeMix("1", "https://sc.test/mix-1", "Updated Title");

            var sut = BuildSut(
                blobMixes: new[] { blobMix },
                rssMixes: new[] { rssMix });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Title, Is.EqualTo("Updated Title"));
        }

        [Test]
        public async Task GetLatestAsync_first_run_empty_blob_returns_rss_and_writes_blob()
        {
            var rssMix = MakeMix("1", "https://sc.test/mix-1");
            var blobRepo = new StubBlobRepository();

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: Array.Empty<Mix>(),
                rssMixes: new[] { rssMix });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
            Assert.That(blobRepo.WrittenMixes, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task GetLatestAsync_rss_failure_falls_back_to_blob_only()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                rssException: new HttpRequestException("RSS down"));

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Url, Is.EqualTo("https://sc.test/mix-1"));
            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task GetLatestAsync_both_sources_fail_returns_empty()
        {
            var sut = BuildSut(
                blobMixes: Array.Empty<Mix>(),
                rssException: new HttpRequestException("RSS down"));

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task GetLatestAsync_no_new_discoveries_skips_write()
        {
            var mix = MakeMix("1", "https://sc.test/mix-1");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { mix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { mix },
                rssMixes: new[] { mix });

            await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task GetLatestAsync_updated_rss_description_triggers_write()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1", description: "Old tracklist line");
            var rssMix = MakeMix("1", "https://sc.test/mix-1", description: "Fixed tracklist line");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { blobMix },
                rssMixes: new[] { rssMix });

            await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetLatestAsync_updated_rss_title_triggers_write()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1", title: "Old Title");
            var rssMix = MakeMix("1", "https://sc.test/mix-1", title: "Fixed Title");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { blobMix },
                rssMixes: new[] { rssMix });

            await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetLatestAsync_caches_result_on_second_call()
        {
            var mix = MakeMix("1", "https://sc.test/mix-1");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { mix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { mix },
                rssMixes: Array.Empty<Mix>());

            await sut.GetLatestAsync(10, CancellationToken.None);
            await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(blobRepo.ReadCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetLatestAsync_caps_result_to_maxItems()
        {
            var blobMixes = new[]
            {
                MakeMix("1", "https://sc.test/mix-1"),
                MakeMix("2", "https://sc.test/mix-2"),
                MakeMix("3", "https://sc.test/mix-3"),
            };

            var sut = BuildSut(blobMixes: blobMixes, rssMixes: Array.Empty<Mix>());

            var result = await sut.GetLatestAsync(maxItems: 2, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task GetLatestAsync_new_rss_mix_prepended_to_front()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1", "Blob Mix");
            var newRssMix = MakeMix("2", "https://sc.test/mix-2", "New RSS Mix");

            var sut = BuildSut(
                blobMixes: new[] { blobMix },
                rssMixes: new[] { newRssMix });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Title, Is.EqualTo("New RSS Mix"));
            Assert.That(result[1].Title, Is.EqualTo("Blob Mix"));
        }

        private static BlobBackedMixCatalogueProvider BuildSut(
            StubBlobRepository? blobRepo = null,
            IReadOnlyList<Mix>? blobMixes = null,
            IReadOnlyList<Mix>? rssMixes = null,
            Exception? rssException = null)
        {
            var repo = blobRepo ?? new StubBlobRepository
            {
                BlobMixes = blobMixes ?? Array.Empty<Mix>(),
            };

            if (blobMixes is not null && blobRepo is null)
            {
                repo.BlobMixes = blobMixes;
            }

            IMixCatalogueProvider rssProvider = rssException is not null
                ? new ThrowingMixCatalogueProvider(rssException)
                : new StubMixCatalogueProvider(rssMixes ?? Array.Empty<Mix>());

            return new BlobBackedMixCatalogueProvider(
                rssProvider,
                repo,
                new MemoryCache(new MemoryCacheOptions()),
                new StubCatalogCacheInvalidator(),
                NullLogger<BlobBackedMixCatalogueProvider>.Instance);
        }

        private static Mix MakeMix(string id, string url, string title = "Test Mix", string? description = null)
        {
            return new Mix
            {
                Id = id,
                Title = title,
                Url = url,
                Genre = "dnb",
                Energy = "peak",
                Description = description,
            };
        }

        private sealed class StubMixCatalogueProvider : IMixCatalogueProvider
        {
            private readonly IReadOnlyList<Mix> _mixes;

            public StubMixCatalogueProvider(IReadOnlyList<Mix> mixes)
            {
                _mixes = mixes;
            }

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
            {
                return Task.FromResult(_mixes);
            }
        }

        private sealed class ThrowingMixCatalogueProvider : IMixCatalogueProvider
        {
            private readonly Exception _exception;

            public ThrowingMixCatalogueProvider(Exception exception)
            {
                _exception = exception;
            }

            public Task<IReadOnlyList<Mix>> GetLatestAsync(int maxItems, CancellationToken cancellationToken)
            {
                throw _exception;
            }
        }

        private sealed class StubCatalogCacheInvalidator : ICatalogCacheInvalidator
        {
            public int Version => 0;

            public void Invalidate()
            {
            }
        }

        private sealed class StubBlobRepository : IBlobMixCatalogueRepository
        {
            public IReadOnlyList<Mix> BlobMixes { get; set; } = Array.Empty<Mix>();

            public IReadOnlyList<Mix>? WrittenMixes { get; private set; }

            public int WriteCallCount { get; private set; }

            public int ReadCallCount { get; private set; }

            public Task<IReadOnlyList<Mix>> ReadAsync(CancellationToken cancellationToken)
            {
                ReadCallCount++;
                return Task.FromResult(BlobMixes);
            }

            public Task WriteAsync(IReadOnlyList<Mix> mixes, CancellationToken cancellationToken)
            {
                WriteCallCount++;
                WrittenMixes = mixes;
                return Task.CompletedTask;
            }
        }
    }
}
