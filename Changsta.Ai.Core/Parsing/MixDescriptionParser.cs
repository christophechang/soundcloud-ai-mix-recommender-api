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
            return SplitDescription(description).Intro;
        }

        /// <summary>
        /// Splits a SoundCloud description into the intro paragraph above the tracklist marker and
        /// the raw track section below it. The single source of truth for tracklist-marker
        /// detection and line-ending normalisation, used by <see cref="ExtractIntro"/> and by
        /// downstream tracklist parsers.
        /// </summary>
        /// <param name="description">The raw description text. <see langword="null"/>, empty, or
        /// whitespace inputs return <c>(null, string.Empty)</c>.</param>
        /// <returns>A tuple whose <c>Intro</c> is the trimmed text above the marker (or
        /// <see langword="null"/> when the description has no marker or the intro half is empty)
        /// and whose <c>TrackSection</c> is the trimmed text below the marker (or
        /// <see cref="string.Empty"/> when no marker was found).</returns>
        public static (string? Intro, string TrackSection) SplitDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return (null, string.Empty);
            }

            string[] lines = description
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            int markerIndex = FindMarkerIndex(lines);

            if (markerIndex < 0)
            {
                return (null, string.Empty);
            }

            string intro = string.Join("\n", lines, 0, markerIndex).Trim();
            string trackSection = string.Join("\n", lines, markerIndex + 1, lines.Length - markerIndex - 1).Trim();

            return (
                string.IsNullOrWhiteSpace(intro) ? null : intro,
                trackSection);
        }

        private static int FindMarkerIndex(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = NormaliseMarkerCandidate(lines[i]);

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

        private static string NormaliseMarkerCandidate(string line)
        {
            string trimmed = line.Trim();

            // Tolerate a trailing colon, e.g. "Tracklist:" — common in numbered / cue-point
            // tracklists — so it is recognised the same as the bare "Tracklist" marker.
            if (trimmed.EndsWith(':'))
            {
                trimmed = trimmed[..^1].TrimEnd();
            }

            return trimmed;
        }
    }
}
