using System;
using System.Collections.Generic;
using System.Text.Json;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class TrackListJsonConverterTests
    {
        private static readonly JsonSerializerOptions Options = BlobMixCatalogueRepository.JsonOptions;

        [Test]
        public void Write_EmitsCuePointSeconds_WhenPresent()
        {
            IReadOnlyList<Track> tracks = new[]
            {
                new Track { Artist = "E.R.N.E.S.T.O", Title = "Caracas", CuePointSeconds = 248 },
            };

            string json = JsonSerializer.Serialize(tracks, Options);

            Assert.That(json, Does.Contain("\"cuePointSeconds\":248"));
        }

        [Test]
        public void Write_OmitsCuePointSeconds_WhenNull()
        {
            IReadOnlyList<Track> tracks = new[]
            {
                new Track { Artist = "Yo Speed", Title = "Bring It Back" },
            };

            string json = JsonSerializer.Serialize(tracks, Options);

            Assert.That(json, Does.Not.Contain("cuePointSeconds"));
        }

        [Test]
        public void RoundTrip_PreservesCuePointSeconds()
        {
            IReadOnlyList<Track> original = new[]
            {
                new Track { Artist = "A", Title = "B", CuePointSeconds = 631 },
                new Track { Artist = "C", Title = "D" },
            };

            string json = JsonSerializer.Serialize(original, Options);
            IReadOnlyList<Track> restored = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)
                ?? Array.Empty<Track>();

            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(restored[0].CuePointSeconds, Is.EqualTo(631));
            Assert.That(restored[1].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Read_ObjectWithoutCuePoint_YieldsNull()
        {
            const string json = "[{\"artist\":\"A\",\"title\":\"B\"}]";

            IReadOnlyList<Track> tracks = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)
                ?? Array.Empty<Track>();

            Assert.That(tracks, Has.Count.EqualTo(1));
            Assert.That(tracks[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Read_LegacyV1StringFormat_StillParses_WithNullCuePoint()
        {
            const string json = "[\"Artist - Title\"]";

            IReadOnlyList<Track> tracks = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)
                ?? Array.Empty<Track>();

            Assert.That(tracks, Has.Count.EqualTo(1));
            Assert.That(tracks[0].Artist, Is.EqualTo("Artist"));
            Assert.That(tracks[0].Title, Is.EqualTo("Title"));
            Assert.That(tracks[0].CuePointSeconds, Is.Null);
        }

        [Test]
        public void Read_MalformedCuePointSeconds_IsToleratedAsNull_WithoutFailingTheRead()
        {
            // A float or out-of-Int32 value must not throw and fail the whole catalog read;
            // it is skipped (cue null) and subsequent tracks still parse.
            const string json =
                "[{\"artist\":\"A\",\"title\":\"B\",\"cuePointSeconds\":248.5}," +
                "{\"artist\":\"C\",\"title\":\"D\",\"cuePointSeconds\":99999999999}]";

            IReadOnlyList<Track> tracks = JsonSerializer.Deserialize<IReadOnlyList<Track>>(json, Options)
                ?? Array.Empty<Track>();

            Assert.That(tracks, Has.Count.EqualTo(2));
            Assert.That(tracks[0].CuePointSeconds, Is.Null);
            Assert.That(tracks[0].Artist, Is.EqualTo("A"));
            Assert.That(tracks[1].CuePointSeconds, Is.Null);
            Assert.That(tracks[1].Title, Is.EqualTo("D"));
        }
    }
}
