using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed class AiMoodWeightEnricher : IMoodWeightEnricher
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ChatClient _chat;
        private readonly ILogger<AiMoodWeightEnricher> _logger;

        public AiMoodWeightEnricher(IOptions<OpenAiOptions> options, ILogger<AiMoodWeightEnricher> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(resolved.ApiKey))
            {
                throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.Model))
            {
                throw new InvalidOperationException("OpenAI:Model is not configured.");
            }

            _chat = new ChatClient(model: resolved.Model, apiKey: resolved.ApiKey);
        }

        public async Task<IReadOnlyDictionary<string, double>> EnrichAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods,
            CancellationToken cancellationToken = default)
        {
            if (newMoods.Count == 0)
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            string prompt = BuildPrompt(existingWeights, newMoods);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are calibrating a mood weight scale. Output must be strict JSON only. Do not wrap output in ``` fences."),
                new UserChatMessage(prompt),
            };

            try
            {
                var stopwatch = Stopwatch.StartNew();

                ChatCompletion completion = await _chat
                    .CompleteChatAsync(messages, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                string rawContent = completion.Content.Count > 0
                    ? completion.Content[0].Text ?? string.Empty
                    : string.Empty;

                stopwatch.Stop();
                _logger.LogInformation(
                    "Mood weight enrichment AI call completed. newMoodCount={NewMoodCount} openAiMs={OpenAiMs}",
                    newMoods.Count,
                    stopwatch.ElapsedMilliseconds);

                IReadOnlyDictionary<string, double> result = ParseResponse(rawContent, newMoods);

                _logger.LogInformation(
                    "Mood weight enrichment parsed. requested={Requested} scored={Scored} moods={Moods}",
                    newMoods.Count,
                    result.Count,
                    string.Join(", ", result.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI mood weight enrichment call failed.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        internal static string BuildPrompt(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are calibrating a mood weight scale where -2.0 = cold/aggressive and +2.0 = warm/euphoric.");
            sb.AppendLine();
            sb.AppendLine("Existing mood weights for context (sorted by value):");

            foreach (var kvp in existingWeights
                .OrderBy(k => k.Value)
                .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(FormattableString.Invariant($"  {kvp.Key}: {kvp.Value:F1}"));
            }

            sb.AppendLine();
            sb.AppendLine("Score the following new moods on the same scale, keeping scores consistent with the existing entries:");
            sb.AppendLine(JsonSerializer.Serialize(newMoods));
            sb.AppendLine();
            sb.Append("Respond with strict JSON only, no explanation: { \"mood\": value, ... }");
            return sb.ToString();
        }

        internal static IReadOnlyDictionary<string, double> ParseResponse(
            string json,
            IReadOnlyList<string> requestedMoods)
        {
            var requested = new HashSet<string>(requestedMoods, StringComparer.OrdinalIgnoreCase);

            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, double>>(json, JsonOptions)
                    ?? new Dictionary<string, double>();

                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in raw)
                {
                    if (requested.Contains(kvp.Key))
                    {
                        result[kvp.Key] = Math.Clamp(kvp.Value, -2.0, 2.0);
                    }
                }

                return result;
            }
            catch (JsonException)
            {
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
