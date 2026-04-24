using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.Recommendations;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Tests.Unit.Recommendations
{
    [TestFixture]
    public sealed class MixRecommendationUseCaseTests
    {
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
                }));

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
    }
}
