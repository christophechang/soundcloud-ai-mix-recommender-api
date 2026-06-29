using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Parsing;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class TracklistExtractor
    {
        // These patterns are anchored at ^ with bounded quantifiers (\d{1,3}, no nested
        // repetition), so they cannot catastrophically backtrack — no match timeout or
        // RegexMatchTimeoutException handling is required.

        // Leading "1." / "12." list number, e.g. "3. Artist - Title".
        private static readonly Regex LineNumberPrefix = new(
            @"^\d+\.\s*",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Bracketed cue point, e.g. "[4:08] " or "[1:02:03] ". Group 1 is the timestamp.
        private static readonly Regex BracketedCuePrefix = new(
            @"^\[\s*(\d{1,3}:[0-5]\d(?::[0-5]\d)?)\s*\]\s*",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Bare cue point, e.g. "04:08 " or "1:02:03 ". Group 1 is the timestamp.
        private static readonly Regex BareCuePrefix = new(
            @"^(\d{1,3}:[0-5]\d(?::[0-5]\d)?)\s+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IReadOnlyList<Track> Extract(string? description)
        {
            (string? _, string trackSection) = MixDescriptionParser.SplitDescription(description);

            if (string.IsNullOrWhiteSpace(trackSection))
            {
                return Array.Empty<Track>();
            }

            string[] lines = trackSection
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var tracks = new List<Track>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string afterNumber = StripLineNumber(line);
                (string body, int? cuePointSeconds) = StripCuePoint(afterNumber);

                int separatorIndex = body.IndexOf(" - ", StringComparison.Ordinal);

                if (separatorIndex < 0)
                {
                    continue;
                }

                string artist = body.Substring(0, separatorIndex).Trim();
                string title = body.Substring(separatorIndex + 3).Trim();

                tracks.Add(new Track
                {
                    Artist = artist,
                    Title = title,
                    CuePointSeconds = cuePointSeconds,
                });
            }

            return tracks;
        }

        private static string StripLineNumber(string line)
        {
            Match match = LineNumberPrefix.Match(line);
            return match.Success ? line[match.Length..] : line;
        }

        // Removes a leading cue point (bracketed "[m:ss]" or bare "m:ss") and returns its value
        // in whole seconds, or null when there is none. A bare timestamp is only treated as a cue
        // point when the remainder still contains an " - " separator — this protects an artist
        // literally named like a timestamp (e.g. "20:20 - Vision").
        private static (string Body, int? CuePointSeconds) StripCuePoint(string line)
        {
            Match bracketed = BracketedCuePrefix.Match(line);
            if (bracketed.Success)
            {
                return (line[bracketed.Length..], ParseCueSeconds(bracketed.Groups[1].Value));
            }

            Match bare = BareCuePrefix.Match(line);
            if (bare.Success)
            {
                string remainder = line[bare.Length..];
                if (remainder.IndexOf(" - ", StringComparison.Ordinal) >= 0)
                {
                    return (remainder, ParseCueSeconds(bare.Groups[1].Value));
                }
            }

            return (line, null);
        }

        private static int ParseCueSeconds(string token)
        {
            string[] parts = token.Split(':');

            if (parts.Length == 2)
            {
                return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 60)
                    + int.Parse(parts[1], CultureInfo.InvariantCulture);
            }

            return (int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600)
                + (int.Parse(parts[1], CultureInfo.InvariantCulture) * 60)
                + int.Parse(parts[2], CultureInfo.InvariantCulture);
        }
    }
}
