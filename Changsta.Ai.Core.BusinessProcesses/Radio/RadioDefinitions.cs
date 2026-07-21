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
        private readonly IReadOnlyDictionary<string, int> _bpmOffsets;

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

            _bpmOffsets = options.Stations.ToDictionary(s => s.Id, s => s.BpmOffset, StringComparer.Ordinal);

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
                    s.BaseBpmTarget,
                    s.WarmthTarget,
                    s.EnergyValues.ToArray()));
        }

        public IReadOnlyList<RadioStation> Stations { get; }

        public string DefaultStationId { get; }

        public IReadOnlyDictionary<SlotKey, SlotConfig> Slots { get; }

        public bool TryGetStationForGenre(string genre, out string stationId) =>
            _genreToStationId.TryGetValue(genre.Trim(), out stationId!);

        public int GetBpmOffset(string stationId) =>
            _bpmOffsets.TryGetValue(stationId, out int offset) ? offset : 0;

        /// <summary>
        /// The slot's base target with the day-of-week adjustment applied. The adjustments are part
        /// of the scoring algorithm, not product tuning, so they stay in <see cref="SlotDefinitions"/>.
        /// </summary>
        public int GetBpmTarget(SlotKey slot, DayBucket day) =>
            Slots[slot].BaseBpmTarget + SlotDefinitions.GetDayBpmAdjustment(day);
    }
}
