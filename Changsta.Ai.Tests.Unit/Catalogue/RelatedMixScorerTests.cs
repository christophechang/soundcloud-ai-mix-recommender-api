using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Catalogue
{
    [TestFixture]
    public sealed class RelatedMixScorerTests
    {
        [Test]
        public void ComputeRelatedMixes_mix_not_related_to_itself()
        {
            var mix = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var mixes = new[] { mix };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes.Should().BeEmpty();
        }

        [Test]
        public void ComputeRelatedMixes_shared_exact_track_scores_highest()
        {
            var sharedTrack = new Track { Artist = "Skeptical", Title = "Sequence" };

            var target = MakeMix(
                "1",
                "https://sc.test/mix-1",
                genre: "dnb",
                tracklist: new[] { sharedTrack });

            var withSharedTrack = MakeMix(
                "2",
                "https://sc.test/mix-2",
                genre: "house",
                tracklist: new[] { sharedTrack });

            var sameGenreOnly = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var mixes = new[] { target, withSharedTrack, sameGenreOnly };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_shared_artist_scores_above_genre_only()
        {
            var target = MakeMix(
                "1",
                "https://sc.test/mix-1",
                genre: "dnb",
                tracklist: new[] { new Track { Artist = "Skeptical", Title = "Sequence" } });

            var sharedArtist = MakeMix(
                "2",
                "https://sc.test/mix-2",
                genre: "house",
                tracklist: new[] { new Track { Artist = "Skeptical", Title = "Different Track" } });

            var sameGenreOnly = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var mixes = new[] { target, sharedArtist, sameGenreOnly };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
            result[0].RelatedMixes[1].Url.Should().Be("https://sc.test/mix-3");
        }

        [Test]
        public void ComputeRelatedMixes_same_genre_included_in_results()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var sameGenre = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");
            var differentGenre = MakeMix("3", "https://sc.test/mix-3", genre: "house", energy: "deep");

            var mixes = new[] { target, sameGenre, differentGenre };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes.Should().ContainSingle()
                .Which.Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_zero_score_mix_excluded()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb", energy: "peak");
            var noMatch = MakeMix("2", "https://sc.test/mix-2", genre: "house", energy: "deep");

            var mixes = new[] { target, noMatch };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes.Should().BeEmpty();
        }

        [Test]
        public void ComputeRelatedMixes_max_six_returned()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");

            var candidates = new Mix[8];
            for (int i = 0; i < 8; i++)
            {
                candidates[i] = MakeMix(
                    (i + 2).ToString(),
                    $"https://sc.test/mix-{i + 2}",
                    genre: "dnb");
            }

            var allMixes = new List<Mix> { target };
            allMixes.AddRange(candidates);

            var result = RelatedMixScorer.ComputeRelatedMixes(allMixes, out _);

            result[0].RelatedMixes.Should().HaveCount(6);
        }

        [Test]
        public void ComputeRelatedMixes_ties_broken_by_published_at_descending()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");

            var older = MakeMix(
                "2",
                "https://sc.test/older",
                genre: "dnb",
                publishedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var newer = MakeMix(
                "3",
                "https://sc.test/newer",
                genre: "dnb",
                publishedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var mixes = new[] { target, older, newer };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/newer");
        }

        [Test]
        public void ComputeRelatedMixes_same_energy_adds_score()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb", energy: "peak");
            var sameGenreAndEnergy = MakeMix("2", "https://sc.test/mix-2", genre: "dnb", energy: "peak");
            var sameGenreOnlyDiffEnergy = MakeMix("3", "https://sc.test/mix-3", genre: "dnb", energy: "deep");

            var mixes = new[] { target, sameGenreAndEnergy, sameGenreOnlyDiffEnergy };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_shared_moods_add_score()
        {
            var target = MakeMix(
                "1",
                "https://sc.test/mix-1",
                genre: "dnb",
                moods: new[] { "dark", "tense" });

            var sharedMoodsAndGenre = MakeMix(
                "2",
                "https://sc.test/mix-2",
                genre: "dnb",
                moods: new[] { "dark", "tense" });

            var genreOnlyNoMoods = MakeMix("3", "https://sc.test/mix-3", genre: "dnb");

            var mixes = new[] { target, sharedMoodsAndGenre, genreOnlyNoMoods };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_bpm_overlap_adds_score()
        {
            var target = MakeMix(
                "1",
                "https://sc.test/mix-1",
                genre: "dnb",
                bpmMin: 170,
                bpmMax: 175);

            var bpmOverlap = MakeMix(
                "2",
                "https://sc.test/mix-2",
                genre: "dnb",
                bpmMin: 173,
                bpmMax: 178);

            var bpmNoOverlap = MakeMix(
                "3",
                "https://sc.test/mix-3",
                genre: "dnb",
                bpmMin: 120,
                bpmMax: 125);

            var mixes = new[] { target, bpmOverlap, bpmNoOverlap };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            result[0].RelatedMixes[0].Url.Should().Be("https://sc.test/mix-2");
        }

        [Test]
        public void ComputeRelatedMixes_ref_carries_title_url_artworkUrl()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var related = MakeMix(
                "2",
                "https://sc.test/mix-2",
                title: "Deep Rollers",
                genre: "dnb",
                artworkUrl: "https://i1.sndcdn.com/artwork.jpg");

            var mixes = new[] { target, related };

            var result = RelatedMixScorer.ComputeRelatedMixes(mixes, out _);

            var ref0 = result[0].RelatedMixes[0];
            ref0.Title.Should().Be("Deep Rollers");
            ref0.Url.Should().Be("https://sc.test/mix-2");
            ref0.ArtworkUrl.Should().Be("https://i1.sndcdn.com/artwork.jpg");
        }

        [Test]
        public void ComputeRelatedMixes_changed_false_when_related_mixes_identical()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var candidate = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");

            var mixes = new[] { target, candidate };

            var firstPass = RelatedMixScorer.ComputeRelatedMixes(mixes, out bool firstChanged);
            firstChanged.Should().BeTrue();

            RelatedMixScorer.ComputeRelatedMixes(firstPass, out bool secondChanged);
            secondChanged.Should().BeFalse();
        }

        [Test]
        public void ComputeRelatedMixes_changed_true_when_related_mixes_empty_on_input()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var candidate = MakeMix("2", "https://sc.test/mix-2", genre: "dnb");

            var mixes = new[] { target, candidate };

            RelatedMixScorer.ComputeRelatedMixes(mixes, out bool changed);

            changed.Should().BeTrue();
        }

        [Test]
        public void ComputeRelatedMixes_changed_true_when_artwork_url_changes()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var candidate = MakeMix("2", "https://sc.test/mix-2", genre: "dnb", artworkUrl: "https://i1.sndcdn.com/old.jpg");

            var firstPass = RelatedMixScorer.ComputeRelatedMixes(new[] { target, candidate }, out _);

            var candidateWithNewArtwork = MakeMix("2", "https://sc.test/mix-2", genre: "dnb", artworkUrl: "https://i1.sndcdn.com/new.jpg");
            var updatedMixes = new[] { firstPass[0], candidateWithNewArtwork };

            RelatedMixScorer.ComputeRelatedMixes(updatedMixes, out bool changed);

            changed.Should().BeTrue();
        }

        [Test]
        public void ComputeRelatedMixes_changed_true_when_title_changes()
        {
            var target = MakeMix("1", "https://sc.test/mix-1", genre: "dnb");
            var candidate = MakeMix("2", "https://sc.test/mix-2", genre: "dnb", title: "Original Title");

            var firstPass = RelatedMixScorer.ComputeRelatedMixes(new[] { target, candidate }, out _);

            var candidateWithNewTitle = MakeMix("2", "https://sc.test/mix-2", genre: "dnb", title: "Updated Title");
            var updatedMixes = new[] { firstPass[0], candidateWithNewTitle };

            RelatedMixScorer.ComputeRelatedMixes(updatedMixes, out bool changed);

            changed.Should().BeTrue();
        }

        private static Mix MakeMix(
            string id,
            string url,
            string title = "Test Mix",
            string genre = "dnb",
            string energy = "peak",
            string? artworkUrl = null,
            IReadOnlyList<Track>? tracklist = null,
            IReadOnlyList<string>? moods = null,
            int? bpmMin = null,
            int? bpmMax = null,
            DateTimeOffset? publishedAt = null)
        {
            return new Mix
            {
                Id = id,
                Title = title,
                Url = url,
                Genre = genre,
                Energy = energy,
                ImageUrl = artworkUrl,
                Tracklist = tracklist ?? Array.Empty<Track>(),
                Moods = moods ?? Array.Empty<string>(),
                BpmMin = bpmMin,
                BpmMax = bpmMax,
                PublishedAt = publishedAt,
            };
        }
    }
}
