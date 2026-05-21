using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal static class RadioStationDefinitions
    {
        internal const string DefaultStationId = "touchdown-fm";

        // Genres use normalized canonical forms produced by GenreNormalizer (all lowercase).
        internal static readonly IReadOnlyList<RadioStation> Stations = new RadioStation[]
        {
            new RadioStation
            {
                Id = "touchdown-fm",
                Name = "Touchdown FM",
                Frequency = "103.5 FM",
                Description = "UK Bass, Garage, Breaks, Hip-Hop and Hardcore",
                IsDefault = true,
                Genres = new[] { "uk bass", "breakbeat", "ukg", "hip-hop", "hardcore" },
            },
            new RadioStation
            {
                Id = "deep-signal-fm",
                Name = "Deep Signal FM",
                Frequency = "97.2 FM",
                Description = "House, Deep House, Electronica, Techno, Disco and Funk",
                Genres = new[] { "house", "deep-house", "electronica", "techno", "disco", "funk" },
            },
            new RadioStation
            {
                Id = "jungle-pressure",
                Name = "Jungle Pressure",
                Frequency = "107.7 FM",
                Description = "Jungle and Drum & Bass",
                Genres = new[] { "jungle", "dnb" },
            },
        };

        // Applied on top of SlotDefinitions.GetBpmTarget so scoring stays meaningful
        // relative to each station's actual BPM range.
        private static readonly IReadOnlyDictionary<string, int> _bpmOffsets =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["touchdown-fm"] = 0,    // garage/breaks align with global targets (110-138)
                ["deep-signal-fm"] = -15,  // house runs 100-125 BPM
                ["jungle-pressure"] = +38, // DNB/Jungle runs 160-180 BPM
            };

        private static readonly IReadOnlyDictionary<string, string> _genreToStationId
            = BuildGenreMap();

        internal static bool TryGetStationForGenre(string genre, out string stationId)
            => _genreToStationId.TryGetValue(genre.Trim(), out stationId!);

        internal static int GetBpmOffset(string stationId)
            => _bpmOffsets.TryGetValue(stationId, out int offset) ? offset : 0;

        private static Dictionary<string, string> BuildGenreMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (RadioStation station in Stations)
            {
                foreach (string genre in station.Genres)
                {
                    map[genre] = station.Id;
                }
            }

            return map;
        }
    }
}
