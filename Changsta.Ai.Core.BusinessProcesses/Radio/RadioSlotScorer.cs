using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal static class RadioSlotScorer
    {
        // Complete list of valid energy values drawn from SlotDefinitions.Slots.
        // Any value outside this set is treated as neutral (2.5 pts) with an audit warning.
        private static readonly HashSet<string> KnownEnergyValues = new(StringComparer.Ordinal)
        {
            "chilled",
            "low",
            "low-mid",
            "mid",
            "journey",
            "mid-high",
            "mid-peak",
            "high",
            "peak",
        };

        /// <summary>
        /// The energy vocabulary the scorer understands. Configuration is validated against this
        /// at startup so an unknown value fails fast rather than silently matching nothing.
        /// </summary>
        internal static bool IsKnownEnergyValue(string energy) => KnownEnergyValues.Contains(energy);

        internal static RadioSlotScore Score(
            Mix mix,
            SlotConfig slot,
            int bpmTarget,
            RadioScoringContext context)
        {
            bool isKnown = !string.IsNullOrEmpty(mix.Energy) && KnownEnergyValues.Contains(mix.Energy);
            double energyScore;
            bool unknownEnergy;
            string? energyWarning;

            if (!isKnown)
            {
                energyScore = 2.5;
                unknownEnergy = true;
                string label = string.IsNullOrEmpty(mix.Energy) ? "(empty)" : mix.Energy;
                energyWarning = $"Unknown energy value '{label}' treated as neutral.";
            }
            else
            {
                energyScore = EnergyMatches(mix.Energy, slot.EnergyValues) ? 5.0 : 0.0;
                unknownEnergy = false;
                energyWarning = null;
            }

            double warmth = mix.Warmth ?? 0.0;
            double warmthScore = Math.Max(0, 4.0 - (Math.Abs(warmth - slot.WarmthTarget) / 0.25));

            int? bpm = mix.GetMidBpm();
            double bpmScore = bpm.HasValue
                ? Math.Max(0, 8.0 - (Math.Abs(bpm.Value - bpmTarget) / 6.0))
                : 0.0;

            double freshnessBonus = context.CrossScheduleUsedIds.Contains(mix.Id) ? 0.0 : 1.0;

            int sameGenreCount = 0;
            foreach (string g in context.RecentGenres)
            {
                if (string.Equals(g, mix.Genre, StringComparison.OrdinalIgnoreCase))
                {
                    sameGenreCount++;
                }
            }

            double genreClusterPenalty = sameGenreCount switch
            {
                1 => 1.5,
                >= 2 => 4.0,
                _ => 0.0,
            };

            string artistKey = ExtractArtistKey(mix.Title);
            double artistPenalty = 0.0;
            foreach (string recent in context.RecentArtists)
            {
                if (string.Equals(recent, artistKey, StringComparison.OrdinalIgnoreCase))
                {
                    artistPenalty = 2.0;
                    break;
                }
            }

            double total = energyScore + warmthScore + bpmScore
                + freshnessBonus - genreClusterPenalty - artistPenalty;

            return new RadioSlotScore
            {
                Total = total,
                EnergyScore = energyScore,
                WarmthScore = warmthScore,
                BpmScore = bpmScore,
                FreshnessBonus = freshnessBonus,
                GenreClusterPenalty = genreClusterPenalty,
                ArtistPenalty = artistPenalty,
                UnknownEnergy = unknownEnergy,
                EnergyWarning = energyWarning,
            };
        }

        internal static string ExtractArtistKey(string title)
        {
            int sep = title.IndexOf(" - ", StringComparison.Ordinal);
            return sep > 0 ? title[..sep].Trim() : title.Trim();
        }

        private static bool EnergyMatches(string energy, string[] energyValues)
        {
            foreach (string e in energyValues)
            {
                if (string.Equals(e, energy, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
