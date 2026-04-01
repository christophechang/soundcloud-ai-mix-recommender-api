using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Normalization
{
    public static class GenreNormalizer
    {
        private static readonly Dictionary<string, string> GenreNormalisations = new(StringComparer.OrdinalIgnoreCase)
        {
            { "deephouse", "deep-house" },
            { "ukbass", "uk-bass" },
        };

        public static string Normalize(string? genre) =>
            string.IsNullOrEmpty(genre) ? string.Empty :
            GenreNormalisations.TryGetValue(genre.Replace("-", string.Empty, StringComparison.Ordinal), out string? canonical)
                ? canonical
                : genre;
    }
}
