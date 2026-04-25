using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
        public async Task GetLatestAsync_normalizes_existing_blob_genres_and_writes_blob()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1", genre: "breaks");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { blobMix },
                rssMixes: Array.Empty<Mix>());

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result[0].Genre, Is.EqualTo("breakbeat"));
            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
            Assert.That(blobRepo.WrittenMixes![0].Genre, Is.EqualTo("breakbeat"));
        }

        [Test]
        public async Task GetLatestAsync_logs_unknown_genres_once_per_sync()
        {
            var blobMix = MakeMix("1", "https://sc.test/mix-1", genre: "Nu Jazz");
            var rssMix = MakeMix("2", "https://sc.test/mix-2", genre: " nu   jazz ");
            var logger = new ListLogger<BlobBackedMixCatalogueProvider>();

            var sut = BuildSut(
                blobMixes: new[] { blobMix },
                rssMixes: new[] { rssMix },
                logger: logger);

            await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(
                logger.Entries.Count(e => e.Level == LogLevel.Warning
                    && e.Message.Contains("Unknown genre value found during catalog sync", StringComparison.Ordinal)
                    && e.Message.Contains("nu jazz", StringComparison.Ordinal)),
                Is.EqualTo(1));
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
        public async Task GetLatestAsync_updated_rss_duration_and_image_refreshes_blob_metadata()
        {
            var blobMix = MakeMix(
                "1",
                "https://sc.test/mix-1",
                duration: "00:30:00",
                imageUrl: "https://img.test/old.png");
            var rssMix = MakeMix(
                "1",
                "https://sc.test/mix-1",
                duration: "00:34:55",
                imageUrl: "https://img.test/new.png");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { blobMix },
                rssMixes: new[] { rssMix });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result[0].Duration, Is.EqualTo("00:34:55"));
            Assert.That(result[0].ImageUrl, Is.EqualTo("https://img.test/new.png"));
            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
            Assert.That(blobRepo.WrittenMixes![0].Duration, Is.EqualTo("00:34:55"));
            Assert.That(blobRepo.WrittenMixes![0].ImageUrl, Is.EqualTo("https://img.test/new.png"));
        }

        [Test]
        public async Task GetLatestAsync_metadata_only_rss_refreshes_existing_blob_mix_without_adding_new_discoveries()
        {
            var blobMix = MakeMix("1", "https://sc.test/legacy-mix", "Legacy Mix");
            var existingRssMix = MakeMetadataOnlyMix(
                "tag:soundcloud,2010:tracks/1",
                "https://sc.test/legacy-mix",
                "Legacy Mix",
                "https://img.test/legacy.png");
            var newMetadataOnlyRssMix = MakeMetadataOnlyMix(
                "tag:soundcloud,2010:tracks/2",
                "https://sc.test/new-legacy-mix",
                "New Legacy Mix",
                "https://img.test/new-legacy.png");
            var blobRepo = new StubBlobRepository { BlobMixes = new[] { blobMix } };

            var sut = BuildSut(
                blobRepo: blobRepo,
                blobMixes: new[] { blobMix },
                rssMixes: new[] { existingRssMix, newMetadataOnlyRssMix });

            var result = await sut.GetLatestAsync(10, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].ImageUrl, Is.EqualTo("https://img.test/legacy.png"));
            Assert.That(result[0].Genre, Is.EqualTo("dnb"));
            Assert.That(result[0].Energy, Is.EqualTo("peak"));
            Assert.That(blobRepo.WriteCallCount, Is.EqualTo(1));
            Assert.That(blobRepo.WrittenMixes, Has.Count.EqualTo(1));
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
            Exception? rssException = null,
            ILogger<BlobBackedMixCatalogueProvider>? logger = null)
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
                logger ?? NullLogger<BlobBackedMixCatalogueProvider>.Instance);
        }

        private static Mix MakeMix(
            string id,
            string url,
            string title = "Test Mix",
            string? description = null,
            string? duration = null,
            string? imageUrl = null,
            string genre = "dnb")
        {
            return new Mix
            {
                Id = id,
                Title = title,
                Url = url,
                Genre = genre,
                Energy = "peak",
                Description = description,
                Duration = duration,
                ImageUrl = imageUrl,
            };
        }

        private static Mix MakeMetadataOnlyMix(
            string id,
            string url,
            string title,
            string imageUrl)
        {
            return new Mix
            {
                Id = id,
                Title = title,
                Url = url,
                Genre = string.Empty,
                Energy = string.Empty,
                ImageUrl = imageUrl,
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

        private sealed class ListLogger<T> : ILogger<T>
        {
            public List<LogEntry> Entries { get; } = new();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
            }

            public sealed class LogEntry
            {
                public LogEntry(LogLevel level, string message)
                {
                    Level = level;
                    Message = message;
                }

                public LogLevel Level { get; }

                public string Message { get; }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
