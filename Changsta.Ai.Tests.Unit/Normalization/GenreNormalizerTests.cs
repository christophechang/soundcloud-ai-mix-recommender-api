using Changsta.Ai.Core.Normalization;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Normalization
{
    [TestFixture]
    public sealed class GenreNormalizerTests
    {
        [TestCase("breaks", "breakbeat")]
        [TestCase("breakbeat", "breakbeat")]
        [TestCase("ukbass", "uk bass")]
        [TestCase("uk-bass", "uk bass")]
        [TestCase("uk bass", "uk bass")]
        [TestCase("drum & bass", "dnb")]
        [TestCase("drum and bass", "dnb")]
        [TestCase("dnb", "dnb")]
        [TestCase("deep house", "deep-house")]
        [TestCase("hip hop", "hip-hop")]
        [TestCase("tech house", "techno")]
        [TestCase("tech-house", "techno")]
        [TestCase("uk garage", "ukg")]
        [TestCase("garage", "ukg")]
        [TestCase("  UK__Bass   Music  ", "uk bass")]
        [TestCase("New Genre", "new genre")]
        public void Normalize_returns_expected_genre(string input, string expected)
        {
            Assert.That(GenreNormalizer.Normalize(input), Is.EqualTo(expected));
        }

        [Test]
        public void Normalize_null_returns_empty_string()
        {
            Assert.That(GenreNormalizer.Normalize(null), Is.Empty);
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Normalize_empty_or_whitespace_returns_empty_string(string input)
        {
            Assert.That(GenreNormalizer.Normalize(input), Is.Empty);
        }
    }
}
