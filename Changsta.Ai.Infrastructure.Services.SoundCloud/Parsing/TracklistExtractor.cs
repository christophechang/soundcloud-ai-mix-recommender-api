using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Parsing;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class TracklistExtractor
    {
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

                int separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);

                if (separatorIndex < 0)
                {
                    continue;
                }

                string artist = line.Substring(0, separatorIndex).Trim();
                string title = line.Substring(separatorIndex + 3).Trim();

                tracks.Add(new Track { Artist = artist, Title = title });
            }

            return tracks;
        }
    }
}
