using System;
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
            Assert.That(result[0], Is.EqualTo("Maverick Sabre, Hedex - I Knew That This Was Love"));
            Assert.That(result[4], Is.EqualTo("Audiomission - Kick Da Flava (VIP)"));
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
            Assert.That(result[0], Is.EqualTo("WZ - Organix"));
            Assert.That(result[2], Is.EqualTo("Megra - I Stay"));
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
    }
}