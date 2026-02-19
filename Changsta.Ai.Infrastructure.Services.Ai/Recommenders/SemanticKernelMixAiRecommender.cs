using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed partial class SemanticKernelMixAiRecommender : IMixAiRecommender
    {
        private const int MixesUpperBound = 50;
        private const int TracklistUpperBound = 30;
        private const int IntroTextUpperBound = 220;
        private const int TagUpperBound = 40;

        private static readonly HashSet<string> TopLevelAllowed = new(StringComparer.Ordinal)
        {
            "results",
            "clarifyingQuestion"
        };

        private static readonly HashSet<string> ResultAllowed = new(StringComparer.Ordinal)
        {
            "mixId",
            "title",
            "url",
            "why",
            "confidence"
        };

        private readonly OpenAiOptions options;

        public SemanticKernelMixAiRecommender(IOptions<OpenAiOptions> options)
        {
            this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(this.options.ApiKey))
            {
                throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(this.options.Model))
            {
                throw new InvalidOperationException("OpenAI:Model is not configured.");
            }
        }

        public async Task<IReadOnlyList<MixAiRecommendation>> RecommendAsync(
            string question,
            IReadOnlyList<Mix> mixes,
            int maxResults,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Question is required.", nameof(question));
            mixes = mixes ?? throw new ArgumentNullException(nameof(mixes));
            if (maxResults <= 0) throw new ArgumentOutOfRangeException(nameof(maxResults));

            var boundedMixes = mixes.Take(MixesUpperBound).ToArray();

            string prompt = BuildPrompt(question, boundedMixes, maxResults);

            var kernel = CreateKernel();
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage("You are a recommendation engine. Output must be strict JSON only. Do not wrap output in ``` fences.");
            history.AddUserMessage(prompt);

            ChatMessageContent result = await chat
                .GetChatMessageContentAsync(history, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string content = NormalizeAiJson(result.Content ?? string.Empty);
            AiResponse parsed = ParseAndValidate(content, boundedMixes, maxResults);

            // Use canonical title/url from Mix objects
            var byId = boundedMixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            return parsed.Results
                .Select(r =>
                {
                    var mix = byId[r.MixId];
                    return new MixAiRecommendation
                    {
                        MixId = r.MixId,
                        Title = mix.Title,
                        Url = mix.Url,
                        Why = r.Why,
                        Confidence = r.Confidence
                    };
                })
                .ToArray();
        }

        private Kernel CreateKernel()
        {
            return Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId: this.options.Model, apiKey: this.options.ApiKey)
                .Build();
        }

        private static string BuildPrompt(string question, IReadOnlyList<Mix> mixes, int maxResults)
        {
            var sb = new StringBuilder();

            void AppendLine(string? s = null) => sb.AppendLine(s ?? string.Empty);

            AppendLine("Task: Recommend mixes that answer the user question using only the mixes provided.");
            AppendLine("Mode: STRICT");
            AppendLine("Rules:");
            AppendLine("1) You must only use mix ids provided in the MIX blocks.");
            AppendLine("2) Output strict JSON only, matching the JSON schema exactly, no extra properties, and do not include ``` anywhere.");
            AppendLine("3) Do not use outside knowledge. Only use evidence from the same mix block you are recommending.");
            AppendLine("4) Evidence may come from intro, tags, or tracklist.");
            AppendLine("5) Every why string MUST be a quoted ANCHOR copied verbatim from that same mix block, and nothing else.");
            AppendLine("6) Allowed why formats are exactly: \"ANCHOR\" or \"ANCHOR\".");
            AppendLine("6b) Example valid why values: \"\\\"broken-beat\\\"\", \"\\\"deep, soulful rollers\\\".\", \"\\\"Lake People - Night Drive\\\"\".");
            AppendLine("6c) Example invalid why values: \"broken-beat\", \"\\\"broken-beat\\\" and more\", \"tags: broken-beat\", \"\\\"broken-beat\\\"!\".");
            AppendLine("7) The ANCHOR must appear verbatim in that mix block intro OR be a single tag token from that mix OR be a substring of a tracklist line from that mix.");
            AppendLine("8) Never output the full tags line as a why string, and never include the literal prefix \"tags:\" in any why string.");
            AppendLine("9) If you cannot produce 2 to 4 valid why strings for a mix, do not include that mix.");
            AppendLine("10) If insufficient evidence exists overall, return zero results.");
            AppendLine("11) results length must be between 0 and " + maxResults + ".");
            AppendLine("12) why must contain 2 to 4 strings.");
            AppendLine("13) confidence must be between 0 and 1.");
            AppendLine("14) title and url are REQUIRED for each result, and MUST be copied exactly from the same MIX block (no edits).");
            AppendLine("15) clarifyingQuestion must be null.");
            AppendLine("16) If any rule is violated, your response will be rejected.");
            AppendLine();
            AppendLine("JSON schema:");
            AppendLine("{ \"results\": [ { \"mixId\": \"...\", \"title\": \"...\", \"url\": \"...\", \"why\": [\"...\"], \"confidence\": 0.0 } ], \"clarifyingQuestion\": null }");
            AppendLine("User question:");
            AppendLine(question);
            AppendLine("Mixes:");

            for (int i = 0; i < mixes.Count; i++)
            {
                var m = mixes[i];
                string intro = TakePrefix(m.Description, IntroTextUpperBound);
                string tracks = string.Join("\n", m.Tracklist.Take(TracklistUpperBound));
                string tags = (m.Tags == null || m.Tags.Count == 0) ? string.Empty : string.Join(" ", m.Tags.Take(TagUpperBound));

                AppendLine("MIX");
                AppendLine("id: " + m.Id);
                AppendLine("title: " + m.Title);
                AppendLine("url: " + m.Url);
                AppendLine("intro: " + intro);
                AppendLine("tags: " + tags);
                AppendLine("tracklist:");
                AppendLine(tracks);
                AppendLine("END_MIX");

                if (i < mixes.Count - 1) AppendLine();
            }

            return sb.ToString().Trim();
        }

        private static string TakePrefix(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string trimmed = text.Trim();
            return trimmed.Length <= maxChars ? trimmed : trimmed.Substring(0, maxChars);
        }

        private static AiResponse ParseAndValidate(string json, IReadOnlyList<Mix> mixes, int maxResults)
        {
            json = NormalizeAiJson(json);

            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("AI returned empty content.");

            EnsureOnlyAllowedJsonProperties(json);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            AiResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<AiResponse>(json, options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("AI response could not be parsed as JSON.", ex);
            }

            if (response?.Results == null) throw new InvalidOperationException("AI response missing results.");
            if (response.ClarifyingQuestion is not null) throw new InvalidOperationException("AI response clarifyingQuestion must be null.");
            if (response.Results.Count > maxResults) throw new InvalidOperationException("AI returned too many results.");

            var allowedById = mixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            foreach (var r in response.Results)
            {
                if (string.IsNullOrWhiteSpace(r.MixId)) throw new InvalidOperationException("AI returned a result with no mixId.");
                if (!allowedById.TryGetValue(r.MixId, out var mix)) throw new InvalidOperationException("AI returned an unknown mixId.");
                if (string.IsNullOrWhiteSpace(r.Title)) throw new InvalidOperationException("AI returned a result with no title.");
                if (string.IsNullOrWhiteSpace(r.Url)) throw new InvalidOperationException("AI returned a result with no url.");
                if (!string.Equals(r.Title, mix.Title, StringComparison.Ordinal)) throw new InvalidOperationException("AI returned a title that does not match the MIX block.");
                if (!string.Equals(r.Url, mix.Url, StringComparison.Ordinal)) throw new InvalidOperationException("AI returned a url that does not match the MIX block.");
                if (r.Why is null || r.Why.Count < 2 || r.Why.Count > 4) throw new InvalidOperationException("AI returned invalid why list.");
                if (r.Why.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("AI returned an empty why string.");

                // Normalize + validate in-place so downstream always sees strict quoted anchors.
                ValidateWhyAnchors(r.Why, mix);

                if (r.Confidence < 0 || r.Confidence > 1) throw new InvalidOperationException("AI returned invalid confidence.");
            }

            return response;
        }

        private static void ValidateWhyAnchors(List<string> why, Mix mix)
        {
            string intro = mix.Description?.Trim() ?? string.Empty;

            var tagTokens = new HashSet<string>(StringComparer.Ordinal);
            if (mix.Tags != null)
            {
                foreach (var t in mix.Tags)
                {
                    var tok = (t ?? string.Empty).Trim();
                    if (tok.Length > 0) tagTokens.Add(tok);
                }
            }

            var trackLines = mix.Tracklist?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? Array.Empty<string>();

            for (int i = 0; i < why.Count; i++)
            {
                string original = why[i];

                if (TryExtractQuotedAnchor(original, out var anchor))
                {
                    EnsureAnchorIsValid(anchor, intro, tagTokens, trackLines);
                    continue;
                }

                // Allow unquoted single-token tag (optionally ending with a period) and normalize into quoted form
                string candidate = original.Trim();
                if (candidate.EndsWith(".", StringComparison.Ordinal)) candidate = candidate[..^1].TrimEnd();

                if (tagTokens.Contains(candidate))
                {
                    why[i] = "\"" + candidate + "\"";
                    EnsureAnchorIsValid(candidate, intro, tagTokens, trackLines);
                    continue;
                }

                throw new InvalidOperationException("AI returned a why string that is not a quoted anchor.");
            }
        }

        private static void EnsureAnchorIsValid(string anchor, string intro, HashSet<string> tagTokens, string[] trackLines)
        {
            if (anchor.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("AI returned a why anchor containing the literal prefix \"tags:\".");
            }

            bool found =
                (!string.IsNullOrEmpty(intro) && intro.Contains(anchor, StringComparison.Ordinal)) ||
                tagTokens.Contains(anchor) ||
                trackLines.Any(line => line.Contains(anchor, StringComparison.Ordinal));

            if (!found) throw new InvalidOperationException("AI returned a why anchor that does not appear in the same MIX block.");
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

        private static string NormalizeAiJson(string content)
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
    }
}