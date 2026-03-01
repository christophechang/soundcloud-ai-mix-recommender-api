using System;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Parsing
{
    [TestFixture]
    public sealed class TracklistExtractorTests
    {
        [Test]
        public void Extract_WhenDescriptionIsNull_ReturnsEmpty()
        {
            var result = TracklistExtractor.Extract(null);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Extract_WhenMarkerIsTracklisting_ExtractsTracks()
        {
            const string description =
                "Tracklisting\n" +
                "Maverick Sabre, Hedex - I Knew That This Was Love\n" +
                "Calibre - Pillow Dub\n" +
                "Calibre & Chelou - E SI O\n" +
                "Break - Last Goodbye (ft. Celestine)\n" +
                "Audiomission - Kick Da Flava (VIP)\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(5));

            Assert.That(result[0].Artist, Is.EqualTo("Maverick Sabre, Hedex"));
            Assert.That(result[0].Title, Is.EqualTo("I Knew That This Was Love"));

            Assert.That(result[1].Artist, Is.EqualTo("Calibre"));
            Assert.That(result[1].Title, Is.EqualTo("Pillow Dub"));

            Assert.That(result[2].Artist, Is.EqualTo("Calibre & Chelou"));
            Assert.That(result[2].Title, Is.EqualTo("E SI O"));

            Assert.That(result[3].Artist, Is.EqualTo("Break"));
            Assert.That(result[3].Title, Is.EqualTo("Last Goodbye (ft. Celestine)"));

            Assert.That(result[4].Artist, Is.EqualTo("Audiomission"));
            Assert.That(result[4].Title, Is.EqualTo("Kick Da Flava (VIP)"));
        }

        [Test]
        public void Extract_WhenMarkerIsTracklist_ExtractsTracks_AndStopsAtNonTrackLine()
        {
            const string description =
                "UK bass pressure focused on groove, weight, and flow\n" +
                "\n" +
                "Tracklist\n" +
                "WZ - Organix\n" +
                "Synkro - No Escape\n" +
                "Megra - I Stay\n" +
                "\n" +
                "❤️ If you like this mix, hit like and repost\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(3));

            Assert.That(result[0].Artist, Is.EqualTo("WZ"));
            Assert.That(result[0].Title, Is.EqualTo("Organix"));

            Assert.That(result[2].Artist, Is.EqualTo("Megra"));
            Assert.That(result[2].Title, Is.EqualTo("I Stay"));
        }

        [Test]
        public void Extract_WhenNoMarker_ReturnsEmpty()
        {
            const string description =
                "No tracklist here\n" +
                "Some other text\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Extract_WhenTrackHasMultipleSeparators_SplitsOnFirstOnly()
        {
            const string description =
                "Tracklist\n" +
                "Artist - Part1 - Part2\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("Artist"));
            Assert.That(result[0].Title, Is.EqualTo("Part1 - Part2"));
        }
    }
}
