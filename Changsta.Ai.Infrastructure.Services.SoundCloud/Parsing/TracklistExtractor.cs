using System;
using System.Collections.Generic;
using System.Linq;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class TracklistExtractor
    {
        private static readonly string[] TracklistMarkers =
        {
            "Tracklist",
            "Tracklisting",
            "Track list",
            "Track listing",
        };

        public static IReadOnlyList<string> Extract(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return Array.Empty<string>();
            }

            (string introText, string trackSection) = SplitIntroAndTrackSection(description);

            if (string.IsNullOrWhiteSpace(trackSection))
            {
                return Array.Empty<string>();
            }

            string[] lines = trackSection
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var tracks = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!line.Contains(" - ", StringComparison.Ordinal))
                {
                    break;
                }

                tracks.Add(line);
            }

            return tracks;
        }

        public static string? ExtractIntroText(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            (string introText, string trackSection) = SplitIntroAndTrackSection(description);

            if (string.IsNullOrWhiteSpace(introText))
            {
                return null;
            }

            return introText;
        }

        private static (string IntroText, string TrackSection) SplitIntroAndTrackSection(string description)
        {
            string[] lines = description
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None);

            int markerIndex = FindMarkerIndex(lines);

            if (markerIndex < 0)
            {
                return (description.Trim(), string.Empty);
            }

            string intro = string.Join("\n", lines.Take(markerIndex)).Trim();
            string trackSection = string.Join("\n", lines.Skip(markerIndex + 1)).Trim();

            return (intro, trackSection);
        }

        private static int FindMarkerIndex(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                for (int j = 0; j < TracklistMarkers.Length; j++)
                {
                    if (string.Equals(line, TracklistMarkers[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }
    }
}