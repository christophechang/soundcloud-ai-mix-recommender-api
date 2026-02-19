using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed class SemanticKernelMixAiRecommender : IMixAiRecommender
    {
        private const int MixesUpperBound = 50;

        private readonly OpenAiOptions options;

        public SemanticKernelMixAiRecommender(IOptions<OpenAiOptions> options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            this.options = options.Value ?? throw new ArgumentNullException(nameof(options));

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
            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException("Question is required.", nameof(question));
            }

            if (mixes is null)
            {
                throw new ArgumentNullException(nameof(mixes));
            }

            if (maxResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResults));
            }

            IReadOnlyList<Mix> boundedMixes = mixes
                .Take(MixesUpperBound)
                .ToArray();

            string prompt = BuildPrompt(question, boundedMixes, maxResults);

            Kernel kernel = CreateKernel();
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage("You are a recommendation engine. Output must be strict JSON only. Do not wrap output in ``` fences.");
            history.AddUserMessage(prompt);

            ChatMessageContent result = await chat
                .GetChatMessageContentAsync(history, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string content = NormalizeAiJson(result.Content ?? string.Empty);

            AiResponse parsed = ParseAndValidate(content, boundedMixes, maxResults);

            // Canonical source of truth for title/url stays in your Mix objects
            var byId = boundedMixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            return parsed.Results
                .Select(r =>
                {
                    Mix mix = byId[r.MixId];

                    return new MixAiRecommendation
                    {
                        MixId = r.MixId,
                        Title = mix.Title,
                        Url = mix.Url,
                        Why = r.Why,
                        Confidence = r.Confidence,
                    };
                })
                .ToArray();
        }

        private Kernel CreateKernel()
        {
            var builder = Kernel.CreateBuilder();

            builder.AddOpenAIChatCompletion(
                modelId: this.options.Model,
                apiKey: this.options.ApiKey);

            return builder.Build();
        }

        private static string BuildPrompt(string question, IReadOnlyList<Mix> mixes, int maxResults)
        {
            const int tracklistUpperBound = 30;
            const int introTextUpperBound = 220;
            const int tagUpperBound = 40;

            var mixBlocks = mixes.Select(m =>
            {
                string intro = TakePrefix(m.Description, introTextUpperBound);
                string tracks = string.Join("\n", m.Tracklist.Take(tracklistUpperBound));

                string tags = m.Tags is null || m.Tags.Count == 0
                    ? string.Empty
                    : string.Join(" ", m.Tags.Take(tagUpperBound));

                return string.Join(
                    "\n",
                    "MIX",
                    "id: " + m.Id,
                    "title: " + m.Title,
                    "url: " + m.Url,
                    "intro: " + intro,
                    "tags: " + tags,
                    "tracklist:",
                    tracks,
                    "END_MIX");
            });

            string mixesText = string.Join("\n\n", mixBlocks);

            return string.Join(
                "\n\n",
                new[]
                {
                    "Task: Recommend mixes that answer the user question using only the mixes provided.",
                    "Mode: STRICT",
                    "Rules:",
                    "1) You must only use mix ids provided in the MIX blocks.",
                    "2) Output strict JSON only, matching the JSON schema exactly, no extra properties, and do not include ``` anywhere.",
                    "3) Do not use outside knowledge. Only use evidence from the same mix block you are recommending.",
                    "4) Evidence may come from intro, tags, or tracklist.",
                    "5) Every why string MUST be a quoted ANCHOR copied verbatim from that same mix block, and nothing else.",
                    "6) Allowed why formats are exactly: \"ANCHOR\" or \"ANCHOR\".",
                    "6b) Example valid why values: \"\\\"broken-beat\\\"\", \"\\\"deep, soulful rollers\\\".\", \"\\\"Lake People - Night Drive\\\"\".",
                    "6c) Example invalid why values: \"broken-beat\", \"\\\"broken-beat\\\" and more\", \"tags: broken-beat\", \"\\\"broken-beat\\\"!\".",
                    "7) The ANCHOR must appear verbatim in that mix block intro OR be a single tag token from that mix OR be a substring of a tracklist line from that mix.",
                    "8) Never output the full tags line as a why string, and never include the literal prefix \"tags:\" in any why string.",
                    "9) If you cannot produce 2 to 4 valid why strings for a mix, do not include that mix.",
                    "10) If insufficient evidence exists overall, return zero results.",
                    "11) results length must be between 0 and " + maxResults + ".",
                    "12) why must contain 2 to 4 strings.",
                    "13) confidence must be between 0 and 1.",
                    "14) title and url are REQUIRED for each result, and MUST be copied exactly from the same MIX block (no edits).",
                    "15) clarifyingQuestion must be null.",
                    "16) If any rule is violated, your response will be rejected."
                }
                .Concat(new[]
                {
                    "JSON schema:",
                    "{ \"results\": [ { \"mixId\": \"...\", \"title\": \"...\", \"url\": \"...\", \"why\": [\"...\"], \"confidence\": 0.0 } ], \"clarifyingQuestion\": null }",
                    "User question:",
                    question,
                    "Mixes:",
                    mixesText
                }));
        }

        private static string TakePrefix(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();

            if (trimmed.Length <= maxChars)
            {
                return trimmed;
            }

            return trimmed.Substring(0, maxChars);
        }

        private static AiResponse ParseAndValidate(string json, IReadOnlyList<Mix> mixes, int maxResults)
        {
            json = NormalizeAiJson(json);

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("AI returned empty content.");
            }

            EnsureOnlyAllowedJsonProperties(json);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            AiResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<AiResponse>(json, options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("AI response could not be parsed as JSON.", ex);
            }

            if (response is null)
            {
                throw new InvalidOperationException("AI response could not be parsed as JSON.");
            }

            if (response.Results is null)
            {
                throw new InvalidOperationException("AI response missing results.");
            }

            if (response.ClarifyingQuestion is not null)
            {
                throw new InvalidOperationException("AI response clarifyingQuestion must be null.");
            }

            if (response.Results.Count > maxResults)
            {
                throw new InvalidOperationException("AI returned too many results.");
            }

            var allowedById = mixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            foreach (AiResult r in response.Results)
            {
                if (string.IsNullOrWhiteSpace(r.MixId))
                {
                    throw new InvalidOperationException("AI returned a result with no mixId.");
                }

                if (!allowedById.TryGetValue(r.MixId, out Mix? mix))
                {
                    throw new InvalidOperationException("AI returned an unknown mixId.");
                }

                if (string.IsNullOrWhiteSpace(r.Title))
                {
                    throw new InvalidOperationException("AI returned a result with no title.");
                }

                if (string.IsNullOrWhiteSpace(r.Url))
                {
                    throw new InvalidOperationException("AI returned a result with no url.");
                }

                if (!string.Equals(r.Title, mix.Title, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("AI returned a title that does not match the MIX block.");
                }

                if (!string.Equals(r.Url, mix.Url, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("AI returned a url that does not match the MIX block.");
                }

                if (r.Why is null || r.Why.Count < 2 || r.Why.Count > 4)
                {
                    throw new InvalidOperationException("AI returned invalid why list.");
                }

                if (r.Why.Any(w => string.IsNullOrWhiteSpace(w)))
                {
                    throw new InvalidOperationException("AI returned an empty why string.");
                }

                // Normalize+validate in-place so downstream always sees strict quoted anchors.
                ValidateWhyAnchors(r.Why, mix);

                if (r.Confidence < 0 || r.Confidence > 1)
                {
                    throw new InvalidOperationException("AI returned invalid confidence.");
                }
            }

            return response;
        }

        // Accept strict quoted anchors.
        // Also accept an unquoted single token ONLY if it exactly matches a tag token,
        // then normalize it into the strict quoted form.
        private static void ValidateWhyAnchors(List<string> why, Mix mix)
        {
            string intro = mix.Description?.Trim() ?? string.Empty;

            var tagTokens = new HashSet<string>(StringComparer.Ordinal);
            if (mix.Tags is not null)
            {
                foreach (string t in mix.Tags)
                {
                    string tok = (t ?? string.Empty).Trim();
                    if (tok.Length > 0)
                    {
                        tagTokens.Add(tok);
                    }
                }
            }

            var trackLines = mix.Tracklist?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? Array.Empty<string>();

            for (int i = 0; i < why.Count; i++)
            {
                string original = why[i];

                // 1) Strict: "ANCHOR" or "ANCHOR".
                if (TryExtractQuotedAnchor(original, out string anchor))
                {
                    EnsureAnchorIsValid(anchor, intro, tagTokens, trackLines);
                    continue;
                }

                // 2) Tolerate: unquoted token that exactly matches a tag token, then normalize.
                string candidate = original.Trim();

                if (candidate.EndsWith(".", StringComparison.Ordinal))
                {
                    candidate = candidate.Substring(0, candidate.Length - 1).TrimEnd();
                }

                if (tagTokens.Contains(candidate))
                {
                    why[i] = "\"" + candidate + "\"";
                    EnsureAnchorIsValid(candidate, intro, tagTokens, trackLines);
                    continue;
                }

                throw new InvalidOperationException("AI returned a why string that is not a quoted anchor.");
            }
        }

        private static void EnsureAnchorIsValid(
            string anchor,
            string intro,
            HashSet<string> tagTokens,
            string[] trackLines)
        {
            if (anchor.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("AI returned a why anchor containing the literal prefix \"tags:\".");
            }

            bool found =
                (intro.Length > 0 && intro.Contains(anchor, StringComparison.Ordinal)) ||
                tagTokens.Contains(anchor) ||
                trackLines.Any(line => line.Contains(anchor, StringComparison.Ordinal));

            if (!found)
            {
                throw new InvalidOperationException("AI returned a why anchor that does not appear in the same MIX block.");
            }
        }

        private static bool TryExtractQuotedAnchor(string value, out string anchor)
        {
            // Allowed formats: "ANCHOR" or "ANCHOR".
            anchor = string.Empty;

            string s = value.Trim();
            bool hasPeriod = s.EndsWith(".", StringComparison.Ordinal);

            if (hasPeriod)
            {
                s = s.Substring(0, s.Length - 1).TrimEnd();
            }

            if (s.Length < 2 || s[0] != '\"' || s[^1] != '\"')
            {
                return false;
            }

            string inner = s.Substring(1, s.Length - 2);
            if (string.IsNullOrWhiteSpace(inner))
            {
                return false;
            }

            anchor = inner;
            return true;
        }

        private static void EnsureOnlyAllowedJsonProperties(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("AI response JSON must be an object.");
            }

            var topLevelAllowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "results",
                "clarifyingQuestion",
            };

            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
            {
                if (!topLevelAllowed.Contains(p.Name))
                {
                    throw new InvalidOperationException("AI response contains extra top-level properties.");
                }
            }

            if (!doc.RootElement.TryGetProperty("results", out JsonElement resultsEl) ||
                resultsEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("AI response missing results array.");
            }

            var resultAllowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "mixId",
                "title",
                "url",
                "why",
                "confidence",
            };

            foreach (JsonElement item in resultsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Each result must be a JSON object.");
                }

                foreach (JsonProperty p in item.EnumerateObject())
                {
                    if (!resultAllowed.Contains(p.Name))
                    {
                        throw new InvalidOperationException("AI response contains extra result properties.");
                    }
                }
            }
        }

        private static string NormalizeAiJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            content = content.TrimStart('\uFEFF').Trim();

            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = content.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    content = content[(firstNewline + 1)..];
                }

                int lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                {
                    content = content[..lastFence];
                }

                content = content.Trim();
            }

            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                content = content.Substring(start, end - start + 1).Trim();
            }

            return content;
        }

        private sealed class AiResponse
        {
            public required List<AiResult> Results { get; init; }

            public string? ClarifyingQuestion { get; init; }
        }

        private sealed class AiResult
        {
            public required string MixId { get; init; }

            public required string Title { get; init; }

            public required string Url { get; init; }

            public required List<string> Why { get; init; }

            public double Confidence { get; init; }
        }
    }
}