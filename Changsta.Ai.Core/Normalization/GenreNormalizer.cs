using System;
using System.Collections.Generic;
using System.Linq;

namespace Changsta.Ai.Core.Normalization
{
    public static class GenreNormalizer
    {
        private static readonly Dictionary<string, string> GenreNormalisations = new(StringComparer.Ordinal)
        {
            { "2 step", "ukg" },
            { "2-step", "ukg" },
            { "bass music", "uk bass" },
            { "break beat", "breakbeat" },
            { "break-beat", "breakbeat" },
            { "break-beats", "breakbeat" },
            { "breakbeat", "breakbeat" },
            { "breakbeats", "breakbeat" },
            { "breaks", "breakbeat" },
            { "deep house", "deep-house" },
            { "deep-house", "deep-house" },
            { "deephouse", "deep-house" },
            { "dnb", "dnb" },
            { "drum & bass", "dnb" },
            { "drum and bass", "dnb" },
            { "drum n bass", "dnb" },
            { "drum'n'bass", "dnb" },
            { "drum-and-bass", "dnb" },
            { "drumandbass", "dnb" },
            { "electronic", "electronica" },
            { "electronic music", "electronica" },
            { "electronica", "electronica" },
            { "garage", "ukg" },
            { "hip hop", "hip-hop" },
            { "hip-hop", "hip-hop" },
            { "hiphop", "hip-hop" },
            { "house", "house" },
            { "jungle", "jungle" },
            { "rap", "hip-hop" },
            { "techno", "techno" },
            { "two-step", "ukg" },
            { "twostep", "ukg" },
            { "uk bass", "uk bass" },
            { "uk bass music", "uk bass" },
            { "uk garage", "ukg" },
            { "uk-bass", "uk bass" },
            { "uk-garage", "ukg" },
            { "uk_bass", "uk bass" },
            { "ukbass", "uk bass" },
            { "ukg", "ukg" },
            { "ukgarage", "ukg" },
        };

        public static string Normalize(string? genre)
        {
            string normalized = NormalizeKey(genre);

            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            return GenreNormalisations.TryGetValue(normalized, out string? canonical)
                ? canonical
                : normalized;
        }

        public static bool IsKnownGenre(string? genre)
        {
            string normalized = NormalizeKey(genre);

            return normalized.Length == 0 || GenreNormalisations.ContainsKey(normalized);
        }

        private static string NormalizeKey(string? genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return string.Empty;
            }

            string lower = genre.Trim().ToLowerInvariant().Replace('_', ' ');
            return string.Join(" ", lower.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
