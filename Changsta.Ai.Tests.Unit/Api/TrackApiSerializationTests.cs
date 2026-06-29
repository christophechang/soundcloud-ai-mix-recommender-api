using System.Text.Json;
using Changsta.Ai.Core.Domain;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Api
{
    [TestFixture]
    public sealed class TrackApiSerializationTests
    {
        // Mirrors the API serialization configured in Changsta.Ai.Interface.Api/Program.cs
        // (camelCase, reflection-based — the domain Track has no custom API converter).
        private static readonly JsonSerializerOptions ApiOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        [Test]
        public void Track_SerialisesCuePointSeconds_InCamelCase_WhenPresent()
        {
            var track = new Track { Artist = "E.R.N.E.S.T.O", Title = "Caracas", CuePointSeconds = 248 };

            string json = JsonSerializer.Serialize(track, ApiOptions);

            Assert.That(json, Does.Contain("\"cuePointSeconds\":248"));
        }

        [Test]
        public void Track_EmitsNullCuePointSeconds_WhenAbsent_SoTheShapeIsStable()
        {
            var track = new Track { Artist = "Yo Speed", Title = "Bring It Back" };

            string json = JsonSerializer.Serialize(track, ApiOptions);

            Assert.That(json, Does.Contain("\"cuePointSeconds\":null"));
        }
    }
}
