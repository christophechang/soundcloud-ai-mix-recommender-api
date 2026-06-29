using Changsta.Ai.Core.Domain;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Domain
{
    [TestFixture]
    public sealed class TrackTests
    {
        [Test]
        public void Equals_IgnoresCuePoint_WhenArtistAndTitleMatch()
        {
            var withCue = new Track { Artist = "Calibre", Title = "Pillow Dub", CuePointSeconds = 248 };
            var withoutCue = new Track { Artist = "Calibre", Title = "Pillow Dub" };

            Assert.That(withCue, Is.EqualTo(withoutCue));
            Assert.That(withCue.GetHashCode(), Is.EqualTo(withoutCue.GetHashCode()));
        }

        [Test]
        public void Equals_DifferentTitle_AreNotEqual()
        {
            var a = new Track { Artist = "Calibre", Title = "Pillow Dub" };
            var b = new Track { Artist = "Calibre", Title = "Mr Majestic" };

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void CuePointSeconds_DefaultsToNull()
        {
            var track = new Track { Artist = "A", Title = "B" };

            Assert.That(track.CuePointSeconds, Is.Null);
        }
    }
}
