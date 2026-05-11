using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    internal static class AiRecommendationResponseValidator
    {
        private static readonly HashSet<string> TopLevelAllowed = new(StringComparer.Ordinal)
        {
            "results",
            "clarifyingQuestion"
        };

        private static readonly HashSet<string> ResultAllowed = new(StringComparer.Ordinal)
        {
            "mixId",
            "reason",
            "why",
            "confidence",
        };

        internal static AiRecommendationResponse ParseAndValidate(string json, IReadOnlyList<Mix> mixes, int maxResults)
        {
            json = NormalizeAiJson(json);

            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("AI returned empty content.");

            EnsureOnlyAllowedJsonProperties(json);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            AiRecommendationResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<AiRecommendationResponse>(json, options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("AI response could not be parsed as JSON.", ex);
            }

            if (response?.Results == null) throw new InvalidOperationException("AI response missing results.");
            if (response.ClarifyingQuestion is not null) throw new InvalidOperationException("AI response clarifyingQuestion must be null.");

            List<AiRecommendationResult> dedupedResults = RemoveDuplicateResults(response.Results);
            if (dedupedResults.Count > maxResults) throw new InvalidOperationException("AI returned too many results.");

            var allowedById = mixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            foreach (var r in dedupedResults)
            {
                if (string.IsNullOrWhiteSpace(r.MixId)) throw new InvalidOperationException("AI returned a result with no mixId.");
                if (!allowedById.TryGetValue(r.MixId, out var mix)) throw new InvalidOperationException("AI returned an unknown mixId.");
                if (string.IsNullOrWhiteSpace(r.Reason)) throw new InvalidOperationException("AI returned a result with no reason.");
                if (r.Reason.Length > 300) throw new InvalidOperationException("AI returned a reason that is too long.");
                if (r.Why is null || r.Why.Count < 1 || r.Why.Count > 4) throw new InvalidOperationException("AI returned invalid why list.");
                if (r.Why.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("AI returned an empty why string.");

                List<string> normalizedWhy = ValidateWhyAnchors(r.Why, mix);
                r.Why.Clear();
                r.Why.AddRange(normalizedWhy);

                if (r.Confidence < 0 || r.Confidence > 1) throw new InvalidOperationException("AI returned invalid confidence.");
            }

            return new AiRecommendationResponse
            {
                Results = dedupedResults,
                ClarifyingQuestion = response.ClarifyingQuestion,
            };
        }

        private static List<AiRecommendationResult> RemoveDuplicateResults(List<AiRecommendationResult> results)
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var deduped = new List<AiRecommendationResult>(results.Count);

            foreach (var result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.MixId) && !seenIds.Add(result.MixId))
                {
                    continue;
                }

                deduped.Add(result);
            }

            return deduped;
        }

        internal static string NormalizeAiJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;

            content = content.TrimStart('\uFEFF').Trim();

            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = content.IndexOf('\n');
                if (firstNewline >= 0) content = content[(firstNewline + 1)..];

                int lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0) content = content[..lastFence];

                content = content.Trim();
            }

            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start) content = content.Substring(start, end - start + 1).Trim();

            return content;
        }

        private static List<string> ValidateWhyAnchors(List<string> why, Mix mix)
        {
            string genre = (mix.Genre ?? string.Empty).Trim();
            string energy = (mix.Energy ?? string.Empty).Trim();

            string bpmAnchor = MixPromptBuilder.FormatBpmAnchor(mix.BpmMin, mix.BpmMax);

            var moodTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in mix.Moods)
            {
                var tok = (m ?? string.Empty).Trim();
                if (tok.Length > 0) moodTokens.Add(tok);
            }

            string[] artists = MixPromptBuilder.BuildArtistAnchors(mix);

            var trackLines = mix.Tracklist
                .Select(t => $"{t.Artist} - {t.Title}")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            var normalized = new List<string>(why.Count);

            for (int i = 0; i < why.Count; i++)
            {
                string original = why[i];

                if (TryExtractQuotedAnchor(original, out var anchor))
                {
                    EnsureAnchorIsValid(anchor, genre, energy, bpmAnchor, moodTokens, artists, trackLines, mix.Id);
                    normalized.Add(original);
                    continue;
                }

                // Allow unquoted single-token mood/genre/energy and normalize into quoted form.
                string candidate = original.Trim();
                if (candidate.EndsWith(".", StringComparison.Ordinal)) candidate = candidate[..^1].TrimEnd();

                if (IsAllowedUnquotedAnchor(candidate, genre, energy, bpmAnchor, moodTokens))
                {
                    EnsureAnchorIsValid(candidate, genre, energy, bpmAnchor, moodTokens, artists, trackLines, mix.Id);
                    normalized.Add("\"" + candidate + "\"");
                    continue;
                }

                throw new InvalidOperationException("AI returned a why string that is not a quoted anchor.");
            }

            return normalized;
        }

        private static bool IsAllowedUnquotedAnchor(
            string candidate,
            string genre,
            string energy,
            string bpmAnchor,
            HashSet<string> moodTokens)
        {
            if (candidate.Length == 0) return false;

            if (moodTokens.Contains(candidate)) return true;

            if (genre.Length > 0 && GenresEquivalent(candidate, genre)) return true;
            if (energy.Length > 0 && string.Equals(candidate, energy, StringComparison.OrdinalIgnoreCase)) return true;

            if (bpmAnchor.Length > 0 && string.Equals(candidate, bpmAnchor, StringComparison.OrdinalIgnoreCase)) return true;

            // also allow "bpm: X" / "bpm: X-Y" as a candidate
            if (bpmAnchor.Length > 0 && string.Equals(candidate, "bpm: " + bpmAnchor, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static void EnsureAnchorIsValid(
            string anchor,
            string genre,
            string energy,
            string bpmAnchor,
            HashSet<string> moodTokens,
            string[] artists,
            string[] trackLines,
            string mixId)
        {
            bool foundInGenre = genre.Length > 0 && GenresEquivalent(anchor, genre);
            bool foundInEnergy = energy.Length > 0 && string.Equals(anchor, energy, StringComparison.OrdinalIgnoreCase);

            bool foundInBpm =
                bpmAnchor.Length > 0 &&
                (string.Equals(anchor, bpmAnchor, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(anchor, "bpm: " + bpmAnchor, StringComparison.OrdinalIgnoreCase));

            bool foundInMoods = moodTokens.Contains(anchor);
            bool foundInArtists = artists.Any(artist => string.Equals(anchor, artist, StringComparison.OrdinalIgnoreCase));

            bool foundInTracklist = trackLines.Any(line => line.Contains(anchor, StringComparison.Ordinal));

            if (!(foundInGenre || foundInEnergy || foundInBpm || foundInMoods || foundInArtists || foundInTracklist))
            {
                throw new InvalidOperationException(
                    "Why anchor not found in MIX block. " +
                    $"mixId='{mixId}', anchor='{anchor}', " +
                    $"foundInGenre={foundInGenre}, foundInEnergy={foundInEnergy}, foundInBpm={foundInBpm}, foundInMoods={foundInMoods}, foundInArtists={foundInArtists}, foundInTracklist={foundInTracklist}.");
            }
        }

        private static bool GenresEquivalent(string anchor, string genre)
        {
            return string.Equals(
                GenreNormalizer.Normalize(anchor),
                GenreNormalizer.Normalize(genre),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractQuotedAnchor(string value, out string anchor)
        {
            anchor = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string s = value.Trim();
            if (s.EndsWith(".", StringComparison.Ordinal)) s = s[..^1].TrimEnd();

            if (s.Length < 2 || s[0] != '\"' || s[^1] != '\"') return false;

            string inner = s[1..^1];
            if (string.IsNullOrWhiteSpace(inner)) return false;

            anchor = inner;
            return true;
        }

        private static void EnsureOnlyAllowedJsonProperties(string json)
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("AI response JSON must be an object.");

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!TopLevelAllowed.Contains(p.Name)) throw new InvalidOperationException("AI response contains extra top-level properties.");
            }

            if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("AI response missing results array.");
            }

            foreach (var item in resultsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Each result must be a JSON object.");
                foreach (var p in item.EnumerateObject())
                {
                    if (!ResultAllowed.Contains(p.Name)) throw new InvalidOperationException("AI response contains extra result properties.");
                }
            }
        }
    }
}
