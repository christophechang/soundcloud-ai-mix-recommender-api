using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Normalization;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    /// <summary>
    /// Fails startup on radio configuration that would only surface later as a broken schedule —
    /// an unknown energy value silently matches nothing, and a non-canonical genre silently
    /// schedules no mixes onto its station.
    /// </summary>
    internal static class RadioOptionsValidator
    {
        /// <summary>Returns every problem found, empty when the configuration is usable.</summary>
        internal static IReadOnlyList<string> Validate(RadioOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var failures = new List<string>();

            ValidateStations(options, failures);
            ValidateSlots(options, failures);

            return failures;
        }

        private static void ValidateStations(RadioOptions options, List<string> failures)
        {
            if (options.Stations.Count == 0)
            {
                failures.Add("Radio:Stations must contain at least one station.");
                return;
            }

            foreach (RadioStationOptions station in options.Stations)
            {
                if (string.IsNullOrWhiteSpace(station.Id))
                {
                    failures.Add("Radio:Stations contains a station with no Id.");
                    continue;
                }

                if (station.Genres.Count == 0)
                {
                    failures.Add($"Radio station '{station.Id}' has no genres, so it can never schedule a mix.");
                }

                foreach (string genre in station.Genres)
                {
                    string normalised = GenreNormalizer.Normalize(genre);

                    if (!string.Equals(normalised, genre, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"Radio station '{station.Id}' genre '{genre}' is not canonical — scheduling matches normalised genres, so use '{normalised}'.");
                    }
                }
            }

            string[] duplicateIds = options.Stations
                .GroupBy(s => s.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            foreach (string id in duplicateIds)
            {
                failures.Add($"Radio station id '{id}' is declared more than once.");
            }

            int defaults = options.Stations.Count(s => s.IsDefault);
            if (defaults != 1)
            {
                failures.Add($"Exactly one radio station must be marked IsDefault, found {defaults}.");
            }
        }

        private static void ValidateSlots(RadioOptions options, List<string> failures)
        {
            var configured = new HashSet<SlotKey>();

            foreach (RadioSlotOptions slot in options.Slots)
            {
                if (!Enum.TryParse(slot.Key, ignoreCase: true, out SlotKey parsed))
                {
                    failures.Add($"Radio slot key '{slot.Key}' is not a known slot.");
                    continue;
                }

                configured.Add(parsed);

                if (slot.EnergyValues.Count == 0)
                {
                    failures.Add($"Radio slot '{slot.Key}' has no energy values, so it can never match a mix.");
                }

                foreach (string energy in slot.EnergyValues)
                {
                    if (!RadioSlotScorer.IsKnownEnergyValue(energy))
                    {
                        failures.Add($"Radio slot '{slot.Key}' uses unknown energy value '{energy}'.");
                    }
                }
            }

            foreach (SlotKey required in Enum.GetValues<SlotKey>())
            {
                if (!configured.Contains(required))
                {
                    failures.Add($"Radio slot '{required}' is missing from configuration.");
                }
            }
        }
    }
}
