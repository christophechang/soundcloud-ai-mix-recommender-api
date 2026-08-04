using System;
using System.Collections.Generic;
using System.Linq;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    /// <summary>
    /// The configured stations and slot targets, resolved once and looked up during scheduling.
    /// Replaces the former hardcoded <c>RadioStationDefinitions</c> / <c>SlotDefinitions</c> data;
    /// the purely algorithmic parts of slot resolution stay in <see cref="SlotDefinitions"/>.
    /// </summary>
    internal sealed class RadioDefinitions
    {
        private readonly IReadOnlyDictionary<string, string> _genreToStationId;

        public RadioDefinitions(RadioOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            Stations = options.Stations
                .Select(s => new RadioStation
                {
                    Id = s.Id,
                    Slug = s.Slug,
                    Strapline = s.Strapline,
                    Name = s.Name,
                    Frequency = s.Frequency,
                    Description = s.Description,
                    IsDefault = s.IsDefault,
                    Genres = s.Genres.ToArray(),
                })
                .ToArray();

            DefaultStationId = options.Stations.FirstOrDefault(s => s.IsDefault)?.Id
                ?? options.Stations.FirstOrDefault()?.Id
                ?? string.Empty;

            var genreMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (RadioStationOptions station in options.Stations)
            {
                foreach (string genre in station.Genres)
                {
                    genreMap[genre] = station.Id;
                }
            }

            _genreToStationId = genreMap;

            Slots = options.Slots.ToDictionary(
                s => Enum.Parse<SlotKey>(s.Key, ignoreCase: true),
                s => new SlotConfig(
                    Enum.Parse<SlotKey>(s.Key, ignoreCase: true),
                    s.Label,
                    s.BpmPercentile,
                    s.WarmthTarget,
                    s.EnergyValues.ToArray()));
        }

        public IReadOnlyList<RadioStation> Stations { get; }

        public string DefaultStationId { get; }

        public IReadOnlyDictionary<SlotKey, SlotConfig> Slots { get; }

        public bool TryGetStationForGenre(string genre, out string stationId) =>
            _genreToStationId.TryGetValue(genre.Trim(), out stationId!);

        /// <summary>
        /// The slot's BPM target for one station, taken from that station's own catalogue: the
        /// slot's percentile of the pool, nudged by the day-of-week adjustment, then clamped back
        /// inside the pool so a target is always a tempo the station can actually play. The
        /// adjustments are part of the scoring algorithm, not product tuning, so they stay in
        /// <see cref="SlotDefinitions"/>.
        /// </summary>
        public int GetBpmTarget(SlotKey slot, DayBucket day, IReadOnlyList<int> stationBpms)
        {
            ArgumentNullException.ThrowIfNull(stationBpms);

            if (stationBpms.Count == 0)
            {
                return 0;
            }

            int[] sorted = stationBpms.OrderBy(b => b).ToArray();
            int target = Percentile(sorted, Slots[slot].BpmPercentile)
                + SlotDefinitions.GetDayBpmAdjustment(day);

            return Math.Clamp(target, sorted[0], sorted[^1]);
        }

        /// <summary>Linear-interpolated percentile over an ascending array.</summary>
        internal static int Percentile(int[] ascending, int percentile)
        {
            if (ascending.Length == 1)
            {
                return ascending[0];
            }

            double rank = (ascending.Length - 1) * (Math.Clamp(percentile, 0, 100) / 100.0);
            int low = (int)Math.Floor(rank);
            int high = Math.Min(low + 1, ascending.Length - 1);

            return (int)Math.Round(ascending[low] + ((ascending[high] - ascending[low]) * (rank - low)));
        }
    }
}
