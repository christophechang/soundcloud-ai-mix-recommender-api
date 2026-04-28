using System;

namespace Changsta.Ai.Core.Parsing
{
    public static class MixDescriptionParser
    {
        private static readonly string[] TracklistMarkers =
        {
            "Tracklist",
            "Tracklisting",
            "Track list",
            "Track listing",
        };

        public static string? ExtractIntro(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            string[] lines = description
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            int markerIndex = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                for (int j = 0; j < TracklistMarkers.Length; j++)
                {
                    if (string.Equals(line, TracklistMarkers[j], StringComparison.OrdinalIgnoreCase))
                    {
                        markerIndex = i;
                        break;
                    }
                }

                if (markerIndex >= 0)
                {
                    break;
                }
            }

            if (markerIndex < 0)
            {
                return null;
            }

            string intro = string.Join("\n", lines, 0, markerIndex).Trim();

            return string.IsNullOrWhiteSpace(intro) ? null : intro;
        }
    }
}
