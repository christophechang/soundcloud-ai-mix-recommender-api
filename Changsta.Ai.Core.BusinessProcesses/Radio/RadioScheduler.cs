using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.BusinessProcesses.NowSpinning;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Exceptions;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioScheduler
    {
        private const double MinScoreThreshold = 4.0;
        private const int RecentGenreWindow = 3;
        private const int RecentArtistWindow = 3;

        internal RadioSchedule Build(IReadOnlyList<Mix> catalogue, DateOnly date)
        {
            DayBucket dayBucket = SlotDefinitions.ResolveDayBucket(
                new DateTime(date.Year, date.Month, date.Day).DayOfWeek);

            var crossScheduleUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stationSlots = new Dictionary<string, IReadOnlyList<RadioScheduledSlot>>();

            for (int si = 0; si < RadioStationDefinitions.Stations.Count; si++)
            {
                RadioStation station = RadioStationDefinitions.Stations[si];
                var stationGenres = new HashSet<string>(station.Genres, StringComparer.OrdinalIgnoreCase);
                List<Mix> eligible = catalogue.Where(m => stationGenres.Contains(m.Genre)).ToList();

                if (eligible.Count == 0)
                {
                    string genres = string.Join(", ", station.Genres);
                    string msg = $"Station '{station.Id}' has no eligible mixes in the catalogue. Expected genres: [{genres}].";
                    throw new RadioStationUnavailableException(station.Id, msg);
                }

                int bpmOffset = RadioStationDefinitions.GetBpmOffset(station.Id);

                stationSlots[station.Id] = BuildStationSchedule(
                    eligible,
                    date,
                    si,
                    dayBucket,
                    bpmOffset,
                    crossScheduleUsed);

                foreach (RadioScheduledSlot slot in stationSlots[station.Id])
                {
                    crossScheduleUsed.Add(slot.Mix.Id);
                }
            }

            return new RadioSchedule { ScheduleDate = date, StationSlots = stationSlots };
        }

        private static IReadOnlyList<RadioScheduledSlot> BuildStationSchedule(
            List<Mix> eligible,
            DateOnly date,
            int stationIndex,
            DayBucket dayBucket,
            int bpmOffset,
            IReadOnlySet<string> crossScheduleUsed)
        {
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recentGenres = new List<string>(RecentGenreWindow);
            var recentArtists = new List<string>(RecentArtistWindow);
            var slots = new List<RadioScheduledSlot>(24);

            for (int hour = 0; hour < 24; hour++)
            {
                SlotKey slotKey = SlotDefinitions.ResolveSlot(hour);
                SlotConfig slotConfig = SlotDefinitions.Slots[slotKey];
                int bpmTarget = SlotDefinitions.GetBpmTarget(slotKey, dayBucket) + bpmOffset;

                RadioScheduledSlot slot = SelectSlot(
                    eligible,
                    hour,
                    date,
                    stationIndex,
                    slotConfig,
                    bpmTarget,
                    usedIds,
                    recentGenres,
                    recentArtists,
                    crossScheduleUsed);

                slots.Add(slot);
                usedIds.Add(slot.Mix.Id);

                recentGenres.Add(slot.Mix.Genre);
                if (recentGenres.Count > RecentGenreWindow)
                {
                    recentGenres.RemoveAt(0);
                }

                recentArtists.Add(RadioSlotScorer.ExtractArtistKey(slot.Mix.Title));
                if (recentArtists.Count > RecentArtistWindow)
                {
                    recentArtists.RemoveAt(0);
                }
            }

            return slots;
        }

        private static RadioScheduledSlot SelectSlot(
            List<Mix> eligible,
            int hour,
            DateOnly date,
            int stationIndex,
            SlotConfig slotConfig,
            int bpmTarget,
            IReadOnlySet<string> usedIds,
            IReadOnlyList<string> recentGenres,
            IReadOnlyList<string> recentArtists,
            IReadOnlySet<string> crossScheduleUsed)
        {
            List<Mix> unused = eligible.Where(m => !usedIds.Contains(m.Id)).ToList();

            var fullCtx = new RadioScoringContext
            {
                RecentGenres = recentGenres,
                RecentArtists = recentArtists,
                CrossScheduleUsedIds = crossScheduleUsed,
            };
            List<(Mix mix, RadioSlotScore score)> candidates =
                ScoreAndFilter(unused, slotConfig, bpmTarget, fullCtx, MinScoreThreshold);

            if (candidates.Count > 0)
            {
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour));
            }

            var noArtistCtx = new RadioScoringContext
            {
                RecentGenres = recentGenres,
                CrossScheduleUsedIds = crossScheduleUsed,
            };
            candidates = ScoreAndFilter(unused, slotConfig, bpmTarget, noArtistCtx, MinScoreThreshold);
            if (candidates.Count > 0)
            {
                string[] stage2Rules = { "Artist repetition penalty relaxed." };
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour), stage2Rules);
            }

            var noClusterCtx = new RadioScoringContext
            {
                CrossScheduleUsedIds = crossScheduleUsed,
            };
            candidates = ScoreAndFilter(unused, slotConfig, bpmTarget, noClusterCtx, MinScoreThreshold);
            if (candidates.Count > 0)
            {
                string[] stage3Rules = { "Artist repetition penalty relaxed.", "Genre clustering penalty relaxed." };
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour), stage3Rules);
            }

            if (unused.Count > 0)
            {
                candidates = ScoreAndFilter(unused, slotConfig, bpmTarget, new RadioScoringContext(), double.MinValue);
                string[] stage4Rules =
                {
                    "Artist repetition penalty relaxed.",
                    "Genre clustering penalty relaxed.",
                    "Score threshold relaxed — picking from any unused mix.",
                };
                return MakeSlot(hour, Pick(candidates, date, stationIndex, hour), stage4Rules);
            }

            candidates = ScoreAndFilter(eligible, slotConfig, bpmTarget, new RadioScoringContext(), double.MinValue);
            string[] stage5Rules =
            {
                "Artist repetition penalty relaxed.",
                "Genre clustering penalty relaxed.",
                "Score threshold relaxed.",
                "Same-station same-day repeat — catalogue too small to avoid.",
            };
            return MakeSlot(hour, Pick(candidates, date, stationIndex, hour), stage5Rules);
        }

        private static List<(Mix mix, RadioSlotScore score)> ScoreAndFilter(
            List<Mix> pool,
            SlotConfig slotConfig,
            int bpmTarget,
            RadioScoringContext ctx,
            double threshold)
        {
            var result = new List<(Mix mix, RadioSlotScore score)>(pool.Count);
            foreach (Mix mix in pool)
            {
                RadioSlotScore score = RadioSlotScorer.Score(mix, slotConfig, bpmTarget, ctx);
                if (score.Total >= threshold)
                {
                    result.Add((mix, score));
                }
            }

            result.Sort((a, b) => b.score.Total.CompareTo(a.score.Total));
            return result;
        }

        private static (Mix mix, RadioSlotScore score) Pick(
            List<(Mix mix, RadioSlotScore score)> candidates,
            DateOnly date,
            int stationIndex,
            int hour)
        {
            int seed = unchecked((date.DayNumber * 1009) + (stationIndex * 97) + (hour * 7));
            var rng = new Random(seed);

            var shuffled = candidates.ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            return shuffled[0];
        }

        private static RadioScheduledSlot MakeSlot(
            int hour,
            (Mix mix, RadioSlotScore score) picked,
            string[]? relaxedRules = null)
        {
            var reasons = new List<string>();
            var warnings = new List<string>();

            if (picked.score.EnergyScore >= 5.0)
            {
                reasons.Add("Strong energy match for this slot.");
            }

            if (picked.score.BpmScore >= 6.0)
            {
                reasons.Add("Good BPM fit.");
            }

            if (picked.score.FreshnessBonus > 0)
            {
                reasons.Add("Not used on any station today.");
            }

            if (picked.score.UnknownEnergy)
            {
                warnings.Add(picked.score.EnergyWarning!);
            }

            return new RadioScheduledSlot
            {
                Hour = hour,
                Mix = picked.mix,
                Score = picked.score,
                AuditReasons = reasons,
                AuditWarnings = warnings,
                RelaxedRules = relaxedRules ?? Array.Empty<string>(),
            };
        }
    }
}
