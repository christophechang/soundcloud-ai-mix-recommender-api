using Changsta.Ai.Core.Parsing;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Parsing
{
    [TestFixture]
    public sealed class MixDescriptionParserTests
    {
        [Test]
        public void ExtractIntro_WhenDescriptionIsNull_ReturnsNull()
        {
            string? result = MixDescriptionParser.ExtractIntro(null);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractIntro_WhenNoTracklistMarker_ReturnsNull()
        {
            const string description =
                "Some description text\n" +
                "Artist - Track Title\n";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractIntro_WhenIntroTextBeforeMarker_ReturnsIntro()
        {
            const string description =
                "Dark UK Bass session with heavy cuts.\n" +
                "\n" +
                "Hit like if you enjoy!\n" +
                "\n" +
                "Tracklist\n" +
                "Artist A - Track One\n" +
                "Artist B - Track Two\n";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.EqualTo("Dark UK Bass session with heavy cuts.\n\nHit like if you enjoy!"));
        }

        [Test]
        public void ExtractIntro_WhenMarkerAtStart_ReturnsNull()
        {
            const string description =
                "Tracklist\n" +
                "Artist A - Track One\n";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractIntro_WhenMarkerIsTracklisting_ReturnsIntro()
        {
            const string description =
                "Great set from last summer.\n" +
                "\n" +
                "Tracklisting\n" +
                "Artist A - Track One\n";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.EqualTo("Great set from last summer."));
        }

        [Test]
        public void ExtractIntro_MarkerCaseInsensitive_ReturnsIntro()
        {
            const string description =
                "Intro text here.\n" +
                "\n" +
                "TRACKLIST\n" +
                "Artist A - Track One\n";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.EqualTo("Intro text here."));
        }

        [Test]
        public void ExtractIntro_WithCarriageReturnOnlyLineEndings_ReturnsIntro()
        {
            const string description =
                "Intro line one.\r" +
                "Intro line two.\r" +
                "Tracklist\r" +
                "Artist A - Track One\r";

            string? result = MixDescriptionParser.ExtractIntro(description);

            Assert.That(result, Is.EqualTo("Intro line one.\nIntro line two."));
        }

        [Test]
        public void SplitDescription_WhenDescriptionIsNull_ReturnsNullIntroAndEmptyTrackSection()
        {
            (string? intro, string trackSection) = MixDescriptionParser.SplitDescription(null);

            Assert.That(intro, Is.Null);
            Assert.That(trackSection, Is.Empty);
        }

        [Test]
        public void SplitDescription_WhenNoMarker_ReturnsNullIntroAndEmptyTrackSection()
        {
            const string description =
                "Just some description\n" +
                "with no marker.\n";

            (string? intro, string trackSection) = MixDescriptionParser.SplitDescription(description);

            Assert.That(intro, Is.Null);
            Assert.That(trackSection, Is.Empty);
        }

        [Test]
        public void SplitDescription_WithMarker_ReturnsIntroAndTrackSection()
        {
            const string description =
                "Intro paragraph.\n" +
                "\n" +
                "Tracklist\n" +
                "Artist A - Track One\n" +
                "Artist B - Track Two\n";

            (string? intro, string trackSection) = MixDescriptionParser.SplitDescription(description);

            Assert.That(intro, Is.EqualTo("Intro paragraph."));
            Assert.That(trackSection, Is.EqualTo("Artist A - Track One\nArtist B - Track Two"));
        }

        [Test]
        public void SplitDescription_WithCarriageReturnOnlyLineEndings_FindsMarkerAndSplitsCleanly()
        {
            const string description =
                "Intro paragraph.\r" +
                "\r" +
                "Tracklist\r" +
                "Artist A - Track One\r" +
                "Artist B - Track Two\r";

            (string? intro, string trackSection) = MixDescriptionParser.SplitDescription(description);

            Assert.That(intro, Is.EqualTo("Intro paragraph."));
            Assert.That(trackSection, Is.EqualTo("Artist A - Track One\nArtist B - Track Two"));
        }
    }
}
