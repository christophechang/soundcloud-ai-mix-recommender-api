using System.Collections.Generic;
using Changsta.Ai.Core.BusinessProcesses.Radio;
using Changsta.Ai.Core.Domain;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Radio
{
    [TestFixture]
    public sealed class RadioSlotScorerTests
    {
        private static readonly SlotConfig Primetime = RadioTestConfig.Definitions.Slots[SlotKey.Primetime];

        [Test]
        public void Known_matching_energy_gives_5_points()
        {
            RadioSlotScore s = Score(MakeMix("peak"), Primetime, 138, Empty());
            s.EnergyScore.Should().BeApproximately(5.0, 0.001);
            s.UnknownEnergy.Should().BeFalse();
            s.EnergyWarning.Should().BeNull();
        }

        [Test]
        public void Known_non_matching_energy_gives_zero_points()
        {
            RadioSlotScore s = Score(MakeMix("chilled"), Primetime, 138, Empty());
            s.EnergyScore.Should().BeApproximately(0.0, 0.001);
            s.UnknownEnergy.Should().BeFalse();
        }

        [Test]
        public void Unknown_energy_gives_2_5_neutral_and_warning()
        {
            RadioSlotScore s = Score(MakeMix("intense"), Primetime, 138, Empty());
            s.EnergyScore.Should().BeApproximately(2.5, 0.001);
            s.UnknownEnergy.Should().BeTrue();
            s.EnergyWarning.Should().Contain("intense");
        }

        [Test]
        public void Null_energy_treated_as_unknown()
        {
            Mix mix = new Mix
            {
                Id = "x",
                Title = "A - B",
                Url = "https://sc.test/x",
                Genre = "dnb",
                Energy = string.Empty,
                BpmMin = 138,
            };
            RadioSlotScore s = Score(mix, Primetime, 138, Empty());
            s.UnknownEnergy.Should().BeTrue();
        }

        [Test]
        public void Null_bpm_gives_zero_bpm_score()
        {
            Mix mix = new Mix
            {
                Id = "x",
                Title = "A - B",
                Url = "https://sc.test/x",
                Genre = "dnb",
                Energy = "peak",
            };
            RadioSlotScore s = Score(mix, Primetime, 138, Empty());
            s.BpmScore.Should().Be(0.0);
        }

        [Test]
        public void Perfect_bpm_gives_8_points()
        {
            // BpmMin=136, BpmMax=140 → midBpm=138 = target → score = 8.0
            RadioSlotScore s = Score(MakeMix("peak", bpm: 136), Primetime, 138, Empty());
            s.BpmScore.Should().BeApproximately(8.0, 0.001);
        }

        [Test]
        public void Bpm_48_away_gives_zero_bpm_points()
        {
            // BpmMin=88, BpmMax=92 → midBpm=90, |90-138|=48 → 8 - 48/6 = 0
            RadioSlotScore s = Score(MakeMix("peak", bpm: 88), Primetime, 138, Empty());
            s.BpmScore.Should().Be(0.0);
        }

        [Test]
        public void Same_genre_in_last_1_slot_applies_1_5_penalty()
        {
            Mix mix = MakeMix("peak", genre: "dnb");
            RadioSlotScore noCtx = Score(mix, Primetime, 138, Empty());
            RadioSlotScore withCtx = Score(
                mix,
                Primetime,
                138,
                new RadioScoringContext { RecentGenres = new[] { "dnb" } });
            (noCtx.Total - withCtx.Total).Should().BeApproximately(1.5, 0.001);
            withCtx.GenreClusterPenalty.Should().BeApproximately(1.5, 0.001);
        }

        [Test]
        public void Same_genre_in_last_2_slots_applies_4_0_penalty()
        {
            Mix mix = MakeMix("peak", genre: "dnb");
            RadioSlotScore withCtx = Score(
                mix,
                Primetime,
                138,
                new RadioScoringContext { RecentGenres = new[] { "dnb", "dnb" } });
            withCtx.GenreClusterPenalty.Should().BeApproximately(4.0, 0.001);
        }

        [Test]
        public void Same_artist_in_recent_slots_applies_2_penalty()
        {
            Mix mix = MakeMix("peak", title: "DJ Rolex - The Bounce");
            RadioSlotScore noCtx = Score(mix, Primetime, 138, Empty());
            RadioSlotScore withCtx = Score(
                mix,
                Primetime,
                138,
                new RadioScoringContext { RecentArtists = new[] { "DJ Rolex" } });
            (noCtx.Total - withCtx.Total).Should().BeApproximately(2.0, 0.001);
            withCtx.ArtistPenalty.Should().BeApproximately(2.0, 0.001);
        }

        [Test]
        public void Title_without_separator_uses_full_title_as_artist_key()
        {
            Mix mix = MakeMix("peak", title: "Fabriclive 27");
            RadioSlotScore s = Score(
                mix,
                Primetime,
                138,
                new RadioScoringContext { RecentArtists = new[] { "Fabriclive 27" } });
            s.ArtistPenalty.Should().BeApproximately(2.0, 0.001);
        }

        [Test]
        public void Mix_not_used_elsewhere_gets_1_freshness_bonus()
        {
            RadioSlotScore s = Score(MakeMix("peak"), Primetime, 138, Empty());
            s.FreshnessBonus.Should().BeApproximately(1.0, 0.001);
        }

        [Test]
        public void Mix_used_elsewhere_today_gets_zero_freshness_bonus()
        {
            Mix mix = MakeMix("peak");
            var ctx = new RadioScoringContext
            {
                CrossScheduleUsedIds = new System.Collections.Generic.HashSet<string> { mix.Id },
            };
            RadioSlotScore s = Score(mix, Primetime, 138, ctx);
            s.FreshnessBonus.Should().BeApproximately(0.0, 0.001);
        }

        [TestCase("DJ Zinc - 138 Trek", "DJ Zinc")]
        [TestCase("Fabio & Grooverider", "Fabio & Grooverider")]
        [TestCase("Andy C - Ram Records", "Andy C")]
        public void ExtractArtistKey_splits_on_dash_separator(string title, string expected)
        {
            RadioSlotScorer.ExtractArtistKey(title).Should().Be(expected);
        }

        private static RadioScoringContext Empty() => new RadioScoringContext();

        private static RadioSlotScore Score(Mix mix, SlotConfig slot, int bpmTarget, RadioScoringContext ctx)
            => RadioSlotScorer.Score(mix, slot, bpmTarget, ctx);

        private static Mix MakeMix(
            string energy,
            int bpm = 138,
            string genre = "dnb",
            string title = "Artist - Mix") => new Mix
            {
                Id = System.Guid.NewGuid().ToString(),
                Title = title,
                Url = "https://sc.test/x",
                Genre = genre,
                Energy = energy,
                BpmMin = bpm,
                BpmMax = bpm + 4,
                Warmth = -0.3,
            };
    }
}
