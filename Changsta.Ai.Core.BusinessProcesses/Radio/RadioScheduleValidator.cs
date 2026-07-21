using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal static class RadioScheduleValidator
    {
        internal static IReadOnlyList<RadioScheduleViolation> Validate(RadioSchedule schedule, RadioDefinitions definitions)
        {
            var v = new List<RadioScheduleViolation>();
            CheckSlotCounts(schedule, v);
            CheckGenreOwnership(schedule, v, definitions);
            CheckSameStationRepeats(schedule, v);
            CheckCrossStationHourConflicts(schedule, v);
            return v;
        }

        private static void CheckSlotCounts(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            foreach (var kvp in s.StationSlots)
            {
                if (kvp.Value.Count != 24)
                {
                    v.Add(new RadioScheduleViolation
                    {
                        StationId = kvp.Key,
                        Rule = RadioScheduleRule.SlotCountMismatch,
                        Description = $"Station has {kvp.Value.Count} slots, expected 24.",
                    });
                }
            }
        }

        private static void CheckGenreOwnership(RadioSchedule s, List<RadioScheduleViolation> v, RadioDefinitions definitions)
        {
            foreach (var kvp in s.StationSlots)
            {
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    if (!definitions.TryGetStationForGenre(slot.Mix.Genre, out string ownerStation)
                        || !string.Equals(ownerStation, kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        v.Add(new RadioScheduleViolation
                        {
                            StationId = kvp.Key,
                            Rule = RadioScheduleRule.GenreMismatch,
                            Hour = slot.Hour,
                            MixId = slot.Mix.Id,
                            Description =
                                $"Hour {slot.Hour}: mix '{slot.Mix.Id}' genre '{slot.Mix.Genre}' does not belong to station '{kvp.Key}'.",
                        });
                    }
                }
            }
        }

        private static void CheckSameStationRepeats(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            foreach (var kvp in s.StationSlots)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    if (!seen.Add(slot.Mix.Id))
                    {
                        v.Add(new RadioScheduleViolation
                        {
                            StationId = kvp.Key,
                            Rule = RadioScheduleRule.SameStationSameDayRepeat,
                            Hour = slot.Hour,
                            MixId = slot.Mix.Id,
                            Description =
                                $"Hour {slot.Hour}: mix '{slot.Mix.Id}' appears more than once on '{kvp.Key}' today.",
                        });
                    }
                }
            }
        }

        private static void CheckCrossStationHourConflicts(RadioSchedule s, List<RadioScheduleViolation> v)
        {
            var hourMap = new Dictionary<int, List<(string station, string mixId)>>();
            foreach (var kvp in s.StationSlots)
            {
                foreach (RadioScheduledSlot slot in kvp.Value)
                {
                    if (!hourMap.TryGetValue(slot.Hour, out var list))
                    {
                        list = new List<(string, string)>();
                        hourMap[slot.Hour] = list;
                    }

                    list.Add((kvp.Key, slot.Mix.Id));
                }
            }

            foreach (var hourEntry in hourMap)
            {
                var byMix = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (station, mixId) in hourEntry.Value)
                {
                    if (!byMix.TryGetValue(mixId, out var stations))
                    {
                        stations = new List<string>();
                        byMix[mixId] = stations;
                    }

                    stations.Add(station);
                }

                foreach (var mixEntry in byMix)
                {
                    if (mixEntry.Value.Count > 1)
                    {
                        v.Add(new RadioScheduleViolation
                        {
                            StationId = string.Join(", ", mixEntry.Value),
                            Rule = RadioScheduleRule.SameHourCrossStationDuplicate,
                            Hour = hourEntry.Key,
                            MixId = mixEntry.Key,
                            Description =
                                $"Hour {hourEntry.Key}: mix '{mixEntry.Key}' appears on multiple stations: {string.Join(", ", mixEntry.Value)}.",
                        });
                    }
                }
            }
        }
    }
}
