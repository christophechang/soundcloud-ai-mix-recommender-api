using System.Collections.Generic;
using System.Text.Json;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Domain
{
    [TestFixture]
    public sealed class MixTests
    {
        [Test]
        public void With_expression_changes_one_field_and_preserves_the_rest()
        {
            Mix original = SampleMix();

            Mix updated = original with { Genre = "deep-house" };

            updated.Genre.Should().Be("deep-house");
            updated.Id.Should().Be(original.Id);
            updated.Title.Should().Be(original.Title);
            updated.Url.Should().Be(original.Url);
            updated.Tracklist.Should().BeEquivalentTo(original.Tracklist);
            updated.Moods.Should().BeEquivalentTo(original.Moods);
            updated.RelatedMixes.Should().BeEquivalentTo(original.RelatedMixes);
            updated.Warmth.Should().Be(original.Warmth);
        }

        [Test]
        public void Json_round_trip_preserves_all_serialised_fields()
        {
            Mix original = SampleMix();

            string json = JsonSerializer.Serialize(original);
            Mix? deserialized = JsonSerializer.Deserialize<Mix>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Should().BeEquivalentTo(original, options => options.Excluding(m => m.Slug));
        }

        private static Mix SampleMix() => new Mix
        {
            Id = "mix-1",
            Title = "Artist - Title",
            Url = "https://soundcloud.test/artist/mix-1",
            Description = "Intro.\nTracklist\nA - B",
            Intro = "Intro.",
            Duration = "01:00:00",
            ImageUrl = "https://img.test/mix-1.png",
            Tracklist = new[] { new Track { Artist = "A", Title = "B" } },
            Genre = "house",
            Energy = "mid",
            BpmMin = 120,
            BpmMax = 124,
            Moods = new[] { "warm" },
            RelatedMixes = new[] { new RelatedMixRef { Title = "Artist - Other", Url = "https://soundcloud.test/artist/mix-2" } },
            Warmth = 0.3,
        };
    }
}
