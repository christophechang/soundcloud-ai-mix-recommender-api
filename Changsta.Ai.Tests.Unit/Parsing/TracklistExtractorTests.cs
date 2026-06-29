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
        public void Extract_WithCarriageReturnOnlyLineEndings_ExtractsTracks()
        {
            const string description =
                "Intro line.\r" +
                "Tracklist\r" +
                "WZ - Organix\r" +
                "Synkro - No Escape\r";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Artist, Is.EqualTo("WZ"));
            Assert.That(result[0].Title, Is.EqualTo("Organix"));
            Assert.That(result[1].Artist, Is.EqualTo("Synkro"));
            Assert.That(result[1].Title, Is.EqualTo("No Escape"));
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

        [Test]
        public void Extract_LineNumbersOnly_ParsesTracksWithNullCuePoint()
        {
            const string description =
                "Tracklist:\n" +
                "1. Yo Speed - Bring It Back [Original Mix]\n" +
                "2. E.R.N.E.S.T.O - Caracas [Original Mix]\n" +
                "3. Bombo Rosa - Números\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0].Artist, Is.EqualTo("Yo Speed"));
            Assert.That(result[0].Title, Is.EqualTo("Bring It Back [Original Mix]"));
            Assert.That(result[0].CuePointSeconds, Is.Null);
            Assert.That(result[1].Artist, Is.EqualTo("E.R.N.E.S.T.O"));
            Assert.That(result[2].Title, Is.EqualTo("Números"));
        }

        [Test]
        public void Extract_LineNumbersWithBracketedCuePoints_ParsesArtistTitleAndSeconds()
        {
            const string description =
                "Tracklist:\n" +
                "1. [0:00] Yo Speed - Bring It Back [Original Mix]\n" +
                "2. [4:08] E.R.N.E.S.T.O - Caracas [Original Mix]\n" +
                "3. [7:21] Bombo Rosa - Números\n" +
                "4. [10:31] Sekret Chadow - Big Breaks [Original Mix]\n" +
                "5. [15:20] Bowser - Shake It [Original Mix]\n" +
                "6. [19:44] BAKEY - JB Riddim\n" +
                "7. [22:44] Deibeat - Rise Up [Freestylers remix]\n" +
                "8. [26:51] 26Rebel MC - Tribal Bass [Original Foundation Mix]\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(8));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[0].Title, Is.EqualTo("Bring It Back [Original Mix]"));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(441));
            Assert.That(result[3].CuePointSeconds, Is.EqualTo(631));
            Assert.That(result[4].CuePointSeconds, Is.EqualTo(920));
            Assert.That(result[5].CuePointSeconds, Is.EqualTo(1184));
            Assert.That(result[6].CuePointSeconds, Is.EqualTo(1364));
            Assert.That(result[7].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[7].Artist, Is.EqualTo("26Rebel MC"));
            Assert.That(result[7].Title, Is.EqualTo("Tribal Bass [Original Foundation Mix]"));
        }

        [Test]
        public void Extract_BracketedCuePointsNoLineNumbers_ParsesSeconds()
        {
            const string description =
                "Tracklist:\n" +
                "[0:00] Yo Speed - Bring It Back [Original Mix]\n" +
                "[4:08] E.R.N.E.S.T.O - Caracas [Original Mix]\n" +
                "[26:51] 26Rebel MC - Tribal Bass [Original Foundation Mix]\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[2].Artist, Is.EqualTo("26Rebel MC"));
        }

        [Test]
        public void Extract_BareCuePointsNoBrackets_ParsesSecondsAndPreservesParenTitles()
        {
            const string description =
                "Tracklist:\n" +
                "00:00 Yo Speed - Bring It Back (Original Mix)\n" +
                "04:08 E.R.N.E.S.T.O - Caracas (Original Mix)\n" +
                "07:21 Bombo Rosa - Números\n" +
                "26:51 26Rebel MC - Tribal Bass (Original Foundation Mix)\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[0].Title, Is.EqualTo("Bring It Back (Original Mix)"));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(441));
            Assert.That(result[3].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[3].Artist, Is.EqualTo("26Rebel MC"));
            Assert.That(result[3].Title, Is.EqualTo("Tribal Bass (Original Foundation Mix)"));
        }

        [Test]
        public void Extract_ExistingPlainFormat_StillYieldsNullCuePoints()
        {
            const string description =
                "Tracklist\n" +
                "Calibre - Pillow Dub\n" +
                "Break - Last Goodbye (ft. Celestine)\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].CuePointSeconds, Is.Null);
            Assert.That(result[1].CuePointSeconds, Is.Null);
            Assert.That(result[1].Title, Is.EqualTo("Last Goodbye (ft. Celestine)"));
        }

        [Test]
        public void Extract_BracketedHoursMinutesSeconds_ParsesSeconds()
        {
            const string description =
                "Tracklist\n" +
                "[1:02:03] Long Artist - Long Track\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(3723));
            Assert.That(result[0].Artist, Is.EqualTo("Long Artist"));
        }

        [Test]
        public void Extract_BareHoursMinutesSeconds_ParsesSeconds()
        {
            const string description =
                "Tracklist\n" +
                "1:02:03 Long Artist - Long Track\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(3723));
        }

        [Test]
        public void Extract_BareTimestampThatIsActuallyAnArtist_IsNotTreatedAsCuePoint()
        {
            // "20:20 - Vision": stripping a bare "20:20 " leaves "- Vision" (no " - "),
            // so it is parsed as artist "20:20", title "Vision", with no cue point.
            const string description =
                "Tracklist\n" +
                "20:20 - Vision\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("20:20"));
            Assert.That(result[0].Title, Is.EqualTo("Vision"));
            Assert.That(result[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Extract_LineNumberThenBareCue_StripsBothAndKeepsDigitArtist()
        {
            const string description =
                "Tracklist\n" +
                "8. 26:51 26Rebel MC - Tribal Bass\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(1611));
            Assert.That(result[0].Artist, Is.EqualTo("26Rebel MC"));
            Assert.That(result[0].Title, Is.EqualTo("Tribal Bass"));
        }

        [Test]
        public void Extract_InvalidSecondsValue_IsNotTreatedAsCuePoint()
        {
            // "4:99" — seconds out of range (not [0-5]\d) — is not a cue point.
            const string description =
                "Tracklist\n" +
                "4:99 Some Artist - Some Title\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].CuePointSeconds, Is.Null);
            Assert.That(result[0].Artist, Is.EqualTo("4:99 Some Artist"));
            Assert.That(result[0].Title, Is.EqualTo("Some Title"));
        }

        [Test]
        public void Extract_BracketedCueWithNoSeparator_DropsLine()
        {
            // A cue point but no " - " separator: the line is not a track and is dropped
            // (the cue is computed then discarded). Locks current behaviour.
            const string description =
                "Tracklist\n" +
                "[4:08] Just A Title With No Artist\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Extract_CrlfLineEndingsWithCuePoints_ParsesSeconds()
        {
            const string description =
                "Tracklist\r\n" +
                "[0:00] Yo Speed - Bring It Back\r\n" +
                "[4:08] E.R.N.E.S.T.O - Caracas\r\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[1].CuePointSeconds, Is.EqualTo(248));
        }

        [Test]
        public void Extract_LineThatIsOnlyANumberOrCue_IsSkipped()
        {
            const string description =
                "Tracklist\n" +
                "3.\n" +
                "[4:08]\n" +
                "Real Artist - Real Title\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("Real Artist"));
            Assert.That(result[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Extract_MixedTracklist_SomeTracksHaveCuePoints_OthersDoNot()
        {
            // A single tracklist where only some lines carry a cue point (bracketed and bare),
            // interleaved with lines that have none. Each track is independent.
            const string description =
                "Tracklist:\n" +
                "[0:00] Yo Speed - Bring It Back\n" +
                "E.R.N.E.S.T.O - Caracas\n" +
                "07:21 Bombo Rosa - Números\n" +
                "BAKEY - JB Riddim\n";

            var result = TracklistExtractor.Extract(description);

            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result[0].CuePointSeconds, Is.EqualTo(0));
            Assert.That(result[1].CuePointSeconds, Is.Null);
            Assert.That(result[2].CuePointSeconds, Is.EqualTo(441));
            Assert.That(result[3].CuePointSeconds, Is.Null);
        }
    }
}
