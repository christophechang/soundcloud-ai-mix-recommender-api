using Changsta.Ai.Core.Normalization;
using Changsta.Ai.Infrastructure.Services.Ai.Recommenders;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Recommenders
{
    [TestFixture]
    public sealed class MixRecommendationQueryAnalyzerGenreTests
    {
        // The analyser detects a genre phrase in free text and resolves its canonical genre via
        // GenreNormalizer (the single source of truth — issue #37). These cases assert the two
        // agree, including the aliases that used to live only in the analyser.
        [TestCase("something liquid drum and bass please", "dnb")]
        [TestCase("got any neurofunk", "dnb")]
        [TestCase("d&b set", "dnb")]
        [TestCase("ragga jungle vibes", "jungle")]
        [TestCase("some idm", "electronica")]
        [TestCase("deep house sunday", "deep-house")]
        [TestCase("uk garage bits", "ukg")]
        public void TryExtractGenreFilter_resolves_canonical_genre_via_GenreNormalizer(string question, string expected)
        {
            bool matched = MixRecommendationQueryAnalyzer.TryExtractGenreFilter(question, out string? genre);

            matched.Should().BeTrue();
            genre.Should().Be(expected);
            genre.Should().Be(GenreNormalizer.Normalize(genre!));
        }
    }
}
