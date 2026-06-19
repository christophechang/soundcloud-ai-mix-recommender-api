using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Recommendations;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Changsta.Ai.Tests.Unit.Recommendations
{
    [TestFixture]
    public sealed class MixRecommendationUseCaseTests
    {
        [Test]
        public async Task RecommendAsync_skips_results_whose_mix_id_is_not_in_the_catalogue()
        {
            var mix = new Mix
            {
                Id = "mix-1",
                Title = "Catalogue Mix",
                Url = "https://soundcloud.test/mix-1",
                Genre = "house",
                Energy = "mid",
            };

            var sut = new MixRecommendationUseCase(
                new StubMixCatalogueProvider(new[] { mix }),
                new StubMixAiRecommender(new[]
                {
                    new MixAiRecommendation
                    {
                        MixId = "mix-1",
                        Title = "Catalogue Mix",
                        Url = "https://soundcloud.test/mix-1",
                        Reason = "Matches.",
                        Why = new[] { "house" },
                        Confidence = 0.9,
                    },
                    new MixAiRecommendation
                    {
                        MixId = "unknown-mix-id",
                        Title = "Phantom",
                        Url = "https://soundcloud.test/phantom",
                        Reason = "Hallucinated.",
                        Why = new[] { "house" },
                        Confidence = 0.7,
                    },
                }),
                NullLogger<MixRecommendationUseCase>.Instance);

            MixRecommendationResponseDto response = await sut
                .RecommendAsync(
                    new MixRecommendationRequestDto
                    {
                        Question = "Give me house",
                        MaxResults = 5,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].MixId, Is.EqualTo("mix-1"));
        }

        [Test]
        public async Task RecommendAsync_returns_empty_results_when_all_ai_ids_are_unknown()
        {
            var mix = new Mix
            {
                Id = "mix-1",
                Title = "Catalogue Mix",
                Url = "https://soundcloud.test/mix-1",
                Genre = "house",
                Energy = "mid",
            };

            var sut = new MixRecommendationUseCase(
                new StubMixCatalogueProvider(new[] { mix }),
                new StubMixAiRecommender(new[]
                {
                    new MixAiRecommendation
                    {
                        MixId = "ghost-1",
                        Title = "Ghost",
                        Url = "https://soundcloud.test/ghost-1",
                        Reason = "Hallucinated.",
                        Why = new[] { "house" },
                        Confidence = 0.5,
                    },
                }),
                NullLogger<MixRecommendationUseCase>.Instance);

            MixRecommendationResponseDto response = await sut
                .RecommendAsync(
                    new MixRecommendationRequestDto
                    {
                        Question = "Give me house",
                        MaxResults = 5,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.Results, Is.Empty);
        }

        [Test]
        public async Task RecommendAsync_hydrates_result_metadata_from_catalogue_mix()
        {
            var mix = new Mix
            {
                Id = "mix-1",
                Title = "Metadata Mix",
                Url = "https://soundcloud.test/metadata-mix",
                Duration = "00:42:16",
                ImageUrl = "https://img.test/metadata-mix.png",
                Genre = "dnb",
                Energy = "peak",
            };

            var sut = new MixRecommendationUseCase(
                new StubMixCatalogueProvider(new[] { mix }),
                new StubMixAiRecommender(new[]
                {
                    new MixAiRecommendation
                    {
                        MixId = "mix-1",
                        Title = "Metadata Mix",
                        Url = "https://soundcloud.test/metadata-mix",
                        Reason = "Matches the request.",
                        Why = new[] { "dnb" },
                        Confidence = 0.9,
                    },
                }),
                NullLogger<MixRecommendationUseCase>.Instance);

            MixRecommendationResponseDto response = await sut
                .RecommendAsync(
                    new MixRecommendationRequestDto
                    {
                        Question = "Give me dnb",
                        MaxResults = 1,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].Duration, Is.EqualTo("00:42:16"));
            Assert.That(response.Results[0].ImageUrl, Is.EqualTo("https://img.test/metadata-mix.png"));
        }

        [Test]
        public async Task RecommendAsync_logs_warning_when_dropping_unknown_mix_id()
        {
            var mix = new Mix
            {
                Id = "mix-1",
                Title = "Catalogue Mix",
                Url = "https://soundcloud.test/mix-1",
                Genre = "house",
                Energy = "mid",
            };

            var logger = new ListLogger<MixRecommendationUseCase>();
            var sut = new MixRecommendationUseCase(
                new StubMixCatalogueProvider(new[] { mix }),
                new StubMixAiRecommender(new[]
                {
                    new MixAiRecommendation
                    {
                        MixId = "ghost-1",
                        Title = "Ghost",
                        Url = "https://soundcloud.test/ghost-1",
                        Reason = "Hallucinated.",
                        Why = new[] { "house" },
                        Confidence = 0.4,
                    },
                }),
                logger);

            const string question = "Give me house";
            await sut
                .RecommendAsync(
                    new MixRecommendationRequestDto
                    {
                        Question = question,
                        MaxResults = 5,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            ListLogger<MixRecommendationUseCase>.LogEntry warning =
                logger.Entries.Single(e => e.Level == LogLevel.Warning);
            Assert.That(warning.Message, Does.Contain("ghost-1"));
            Assert.That(warning.Message, Does.Contain(question.Length.ToString()));
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

        private sealed class StubMixAiRecommender : IMixAiRecommender
        {
            private readonly IReadOnlyList<MixAiRecommendation> _recommendations;

            public StubMixAiRecommender(IReadOnlyList<MixAiRecommendation> recommendations)
            {
                _recommendations = recommendations;
            }

            public Task<IReadOnlyList<MixAiRecommendation>> RecommendAsync(
                string question,
                IReadOnlyList<Mix> mixes,
                int maxResults,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(_recommendations);
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
