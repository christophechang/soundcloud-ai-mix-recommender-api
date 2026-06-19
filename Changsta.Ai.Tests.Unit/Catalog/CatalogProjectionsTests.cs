using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Interface.Api.Catalog;
using Changsta.Ai.Interface.Api.ViewModels;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalog
{
    [TestFixture]
    public sealed class CatalogProjectionsTests
    {
        [Test]
        public void GenreNames_normalises_distinct_and_sorts()
        {
            var mixes = new[]
            {
                MakeMix("1", "deep house"),
                MakeMix("2", "Deep-House"),
                MakeMix("3", "dnb"),
            };

            CatalogProjections.GenreNames(mixes).Should().Equal("deep-house", "dnb");
        }

        [Test]
        public void GenreTree_groups_by_genre_then_artist_with_sorted_tracks()
        {
            var mixes = new[]
            {
                MakeMix("1", "house", ("Zed", "B Track"), ("Zed", "A Track")),
            };

            GenreEntry[] tree = CatalogProjections.GenreTree(mixes, genre: null);

            tree.Should().ContainSingle();
            tree[0].Genre.Should().Be("house");
            tree[0].Artists.Should().ContainSingle();
            tree[0].Artists[0].Name.Should().Be("Zed");
            tree[0].Artists[0].Tracks.Should().Equal("A Track", "B Track");
        }

        [Test]
        public void GenreTree_filters_by_normalised_genre()
        {
            var mixes = new[]
            {
                MakeMix("1", "house", ("A", "x")),
                MakeMix("2", "dnb", ("B", "y")),
            };

            GenreEntry[] tree = CatalogProjections.GenreTree(mixes, genre: "house");

            tree.Select(t => t.Genre).Should().Equal("house");
        }

        [Test]
        public void TrackSummaries_counts_recurrence_and_collects_genres()
        {
            var mixes = new[]
            {
                MakeMix("1", "house", ("Artist", "Hit")),
                MakeMix("2", "dnb", ("artist", "hit")),
            };

            TrackSummary[] summaries = CatalogProjections.TrackSummaries(mixes);

            summaries.Should().ContainSingle();
            summaries[0].RecurrenceCount.Should().Be(2);
            summaries[0].GenresSeen.Should().BeEquivalentTo(new[] { "house", "dnb" });
        }

        private static Mix MakeMix(string id, string genre, params (string Artist, string Title)[] tracks) => new Mix
        {
            Id = id,
            Title = $"Mix {id}",
            Url = $"https://sc.test/mix-{id}",
            Genre = genre,
            Energy = "mid",
            Tracklist = tracks.Select(t => new Track { Artist = t.Artist, Title = t.Title }).ToArray(),
        };
    }
}
