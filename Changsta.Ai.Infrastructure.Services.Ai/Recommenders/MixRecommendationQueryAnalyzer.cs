using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    internal static class MixRecommendationQueryAnalyzer
    {
        private const int BpmQueryMin = 100;
        private const int BpmQueryMax = 200;
        private const int BpmTolerance = 10;
        private const int MaxBpmRegexQuestionLength = 500;

        // Matches a BPM signal embedded in a longer question.
        // Alt 1: context word + number 100-200  e.g. "around 130", "at 174", "~130"
        // Alt 2: number 100-200 + "bpm" suffix  e.g. "130bpm", "174 bpm"
        private static readonly Regex BpmInMixedQueryPattern = new(
            @"(?:around|at|about|@|~)\s*(1\d{2}|200)(?!\d)|(?<!\w)(1\d{2}|200)\s*bpm",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            matchTimeout: TimeSpan.FromMilliseconds(100));

        // Words that carry no secondary signal — stripped when deciding if a genre query is pure.
        private static readonly HashSet<string> GenreQueryFillerWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "all", "and", "any", "for", "give", "good", "i", "in",
            "me", "mix", "mixes", "music", "my", "of", "on", "or",
            "playlist", "please", "set", "sets", "show", "some",
            "the", "want", "with",
        };

        private static readonly string[] TrackSpecificHints = new[]
        {
            "track",
            "tracks",
            "song",
            "songs",
            "tune",
            "tunes",
            "title",
            "titles",
            "feat.",
            "featuring",
        };

        // Pre-sorted longest-first so multi-word phrases ("drum and bass") match before
        // their shorter substrings ("dnb"). Each tuple is (query alias, catalog genre value).
        private static readonly (string Alias, string Genre)[] GenreAliasesByLength = new[]
        {
            ("liquid drum and bass", "dnb"),
            ("drum and bass", "dnb"),
            ("drum & bass", "dnb"),
            ("ragga jungle", "jungle"),
            ("electronica", "electronica"),
            ("tech house", "techno"),
            ("tech-house", "techno"),
            ("break beat", "breakbeat"),
            ("deep-house", "deep-house"),
            ("deep house", "deep-house"),
            ("uk garage", "ukg"),
            ("breakbeat", "breakbeat"),
            ("neurofunk", "dnb"),
            ("two step", "ukg"),
            ("uk-bass", "uk bass"),
            ("uk bass", "uk bass"),
            ("hip-hop", "hip-hop"),
            ("hip hop", "hip-hop"),
            ("jungle", "jungle"),
            ("techno", "techno"),
            ("breaks", "breakbeat"),
            ("hiphop", "hip-hop"),
            ("garage", "ukg"),
            ("house", "house"),
            ("2step", "ukg"),
            ("2-step", "ukg"),
            ("d&b", "dnb"),
            ("ukg", "ukg"),
            ("dnb", "dnb"),
            ("idm", "electronica"),
        };

        public static MixRecommendationQueryAnalysis Analyze(string question, IReadOnlyList<Mix> mixes)
        {
            bool isBpmQuery = TryParseBpmQuery(question, out int detectedBpm);
            bool hasBpmComponent = !isBpmQuery && TryExtractBpmFromMixedQuery(question, out detectedBpm);
            Mix[] filteredMixes = (isBpmQuery || hasBpmComponent)
                ? FilterByBpm(mixes, detectedBpm)
                : mixes.ToArray();

            string? detectedGenre = null;
            bool isPureGenreQuery = false;
            if (TryExtractGenreFilter(question, out string? extractedGenre, out string? matchedAlias)
                && extractedGenre is not null
                && matchedAlias is not null)
            {
                Mix[] genreFiltered = FilterByGenre(filteredMixes, extractedGenre);
                if (genreFiltered.Length > 0)
                {
                    filteredMixes = genreFiltered;
                    detectedGenre = extractedGenre;
                    isPureGenreQuery = IsPureGenreQuery(question, matchedAlias);
                }
            }

            return new MixRecommendationQueryAnalysis
            {
                FilteredMixes = filteredMixes,
                PureBpmQuery = isBpmQuery ? detectedBpm : null,
                DetectedGenre = detectedGenre,
                IsPureGenreQuery = isPureGenreQuery,
                IncludeTrackTitles = IsTrackSpecificQuery(question),
            };
        }

        internal static bool IsTrackSpecificQuery(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return false;
            }

            if (question.Contains("\"", StringComparison.Ordinal) || question.Contains('\'', StringComparison.Ordinal))
            {
                return true;
            }

            return TrackSpecificHints.Any(hint => question.Contains(hint, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryParseBpmQuery(string question, out int bpm)
        {
            bpm = 0;
            var s = question.Trim();

            if (s.EndsWith("bpm", StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^3].Trim();
            }

            if (!int.TryParse(s, out int parsed) || parsed < BpmQueryMin || parsed > BpmQueryMax)
            {
                return false;
            }

            bpm = parsed;
            return true;
        }

        internal static bool TryExtractBpmFromMixedQuery(string question, out int bpm)
        {
            bpm = 0;

            if (string.IsNullOrWhiteSpace(question) || question.Length > MaxBpmRegexQuestionLength)
            {
                return false;
            }

            Match match;

            try
            {
                match = BpmInMixedQueryPattern.Match(question);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }

            if (!match.Success) return false;

            string raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            bpm = int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        internal static Mix[] FilterByBpm(IReadOnlyList<Mix> mixes, int targetBpm)
        {
            return mixes
                .Where(m =>
                {
                    if (m.BpmMin is null && m.BpmMax is null) return false;

                    int lo = m.BpmMin ?? m.BpmMax!.Value;
                    int hi = m.BpmMax ?? m.BpmMin!.Value;

                    return targetBpm >= lo - BpmTolerance && targetBpm <= hi + BpmTolerance;
                })
                .ToArray();
        }

        internal static bool TryExtractGenreFilter(string question, out string? genre) =>
            TryExtractGenreFilter(question, out genre, out _);

        internal static bool TryExtractGenreFilter(string question, out string? genre, out string? matchedAlias)
        {
            genre = null;
            matchedAlias = null;

            if (string.IsNullOrWhiteSpace(question))
            {
                return false;
            }

            string q = question.Trim();

            foreach (var (alias, canonicalGenre) in GenreAliasesByLength)
            {
                if (q.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    genre = canonicalGenre;
                    matchedAlias = alias;
                    return true;
                }
            }

            return false;
        }

        internal static bool IsPureGenreQuery(string question, string matchedAlias)
        {
            string remaining = question
                .Replace(matchedAlias, string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            string[] tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return !tokens.Any(t => !GenreQueryFillerWords.Contains(t));
        }

        internal static Mix[] FilterByGenre(IReadOnlyList<Mix> mixes, string genre)
        {
            return mixes
                .Where(m => string.Equals(
                    GenreNormalizer.Normalize(m.Genre),
                    GenreNormalizer.Normalize(genre),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }
}
