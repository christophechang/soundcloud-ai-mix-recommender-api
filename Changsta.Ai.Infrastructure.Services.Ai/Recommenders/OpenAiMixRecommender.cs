using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Ai.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    public sealed class OpenAiMixRecommender : IMixAiRecommender
    {
        private const int MixesUpperBound = 100;
        private const int MaxRetryAttempts = 2;
        private const int RecommendationCacheTtlMinutes = 60;

        private readonly ChatClient chat;
        private readonly IMemoryCache cache;
        private readonly ILogger<OpenAiMixRecommender> logger;

        public OpenAiMixRecommender(IOptions<OpenAiOptions> options, IMemoryCache cache, ILogger<OpenAiMixRecommender> logger)
        {
            var resolvedOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(resolvedOptions.ApiKey))
            {
                throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
            }

            if (string.IsNullOrWhiteSpace(resolvedOptions.Model))
            {
                throw new InvalidOperationException("OpenAI:Model is not configured.");
            }

            this.chat = new ChatClient(model: resolvedOptions.Model, apiKey: resolvedOptions.ApiKey);
        }

        public async Task<IReadOnlyList<MixAiRecommendation>> RecommendAsync(
            string question,
            IReadOnlyList<Mix> mixes,
            int maxResults,
            CancellationToken cancellationToken)
        {
            var totalStopwatch = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Question is required.", nameof(question));
            mixes = mixes ?? throw new ArgumentNullException(nameof(mixes));
            if (maxResults <= 0) throw new ArgumentOutOfRangeException(nameof(maxResults));

            string cacheKey = BuildCacheKey(question, maxResults);
            if (this.cache.TryGetValue(cacheKey, out IReadOnlyList<MixAiRecommendation>? cached) && cached != null)
            {
                totalStopwatch.Stop();
                this.logger.LogInformation(
                    "Recommendation cache hit. questionLength={QuestionLength} maxResults={MaxResults} resultCount={ResultCount} totalMs={TotalMs}",
                    question.Length,
                    maxResults,
                    cached.Count,
                    totalStopwatch.ElapsedMilliseconds);
                return cached;
            }

            var boundedMixes = mixes.Take(MixesUpperBound).ToArray();
            MixRecommendationQueryAnalysis analysis = MixRecommendationQueryAnalyzer.Analyze(question, boundedMixes);
            (Mix[] promptMixes, Dictionary<string, string> promptIdToRealId) = MixPromptBuilder.BuildPromptMixes(analysis.FilteredMixes);
            string prompt = MixPromptBuilder.BuildPrompt(
                question,
                promptMixes,
                maxResults,
                analysis.PureBpmQuery,
                analysis.DetectedGenre,
                analysis.IsPureGenreQuery,
                analysis.IncludeTrackTitles);

            this.logger.LogInformation(
                "Recommendation request prepared. questionLength={QuestionLength} maxResults={MaxResults} mixCount={MixCount} trackSpecific={TrackSpecific} promptMode={PromptMode} promptLength={PromptLength}",
                question.Length,
                maxResults,
                analysis.FilteredMixes.Length,
                analysis.IncludeTrackTitles,
                analysis.IncludeTrackTitles ? "tracklist" : "artists",
                prompt.Length);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a recommendation engine. Output must be strict JSON only. Do not wrap output in ``` fences."),
                new UserChatMessage(prompt),
            };

            var byId = promptMixes.ToDictionary(m => m.Id, StringComparer.Ordinal);

            Exception? lastException = null;
            string rawContent = string.Empty;

            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                var attemptStopwatch = Stopwatch.StartNew();

                try
                {
                    ChatCompletion completion = await chat
                        .CompleteChatAsync(messages, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    rawContent = completion.Content.Count > 0
                        ? completion.Content[0].Text ?? string.Empty
                        : string.Empty;

                    string content = AiRecommendationResponseValidator.NormalizeAiJson(rawContent);
                    AiRecommendationResponse parsed = AiRecommendationResponseValidator.ParseAndValidate(content, promptMixes, maxResults);

                    MixAiRecommendation[] results = parsed.Results
                        .Select(r =>
                        {
                            var mix = byId[r.MixId];
                            return new MixAiRecommendation
                            {
                                MixId = promptIdToRealId[r.MixId],
                                Title = mix.Title,
                                Url = mix.Url,
                                Reason = r.Reason,
                                Why = r.Why,
                                Confidence = r.Confidence
                            };
                        })
                        .ToArray();

                    if (results.Length > 0)
                    {
                        this.cache.Set(cacheKey, (IReadOnlyList<MixAiRecommendation>)results, TimeSpan.FromMinutes(RecommendationCacheTtlMinutes));
                    }

                    attemptStopwatch.Stop();
                    totalStopwatch.Stop();
                    this.logger.LogInformation(
                        "Recommendation request succeeded. attempt={Attempt} resultCount={ResultCount} openAiMs={OpenAiMs} totalMs={TotalMs}",
                        attempt,
                        results.Length,
                        attemptStopwatch.ElapsedMilliseconds,
                        totalStopwatch.ElapsedMilliseconds);
                    return results;
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException or HttpRequestException)
                {
                    attemptStopwatch.Stop();
                    lastException = ex;
                    this.logger.LogInformation(
                        "Recommendation attempt failed. attempt={Attempt} maxAttempts={MaxAttempts} openAiMs={OpenAiMs} failureType={FailureType} message={FailureMessage}",
                        attempt,
                        MaxRetryAttempts,
                        attemptStopwatch.ElapsedMilliseconds,
                        ex.GetType().Name,
                        ex.Message);
                    this.logger.LogWarning(
                        ex,
                        "AI recommendation attempt {Attempt}/{MaxAttempts} failed.",
                        attempt,
                        MaxRetryAttempts);

                    if (attempt < MaxRetryAttempts)
                    {
                        if (rawContent.Length > 0)
                        {
                            messages.Add(new AssistantChatMessage(rawContent));
                            messages.Add(new UserChatMessage(
                                "Your response failed validation: " + ex.Message +
                                " Fix only the specific problem and respond with strict JSON only."));
                        }
                    }
                }
            }

            this.logger.LogError(
                lastException,
                "All {MaxAttempts} AI recommendation attempts failed. Returning empty results.",
                MaxRetryAttempts);
            totalStopwatch.Stop();
            this.logger.LogInformation(
                "Recommendation request failed after retries. maxAttempts={MaxAttempts} totalMs={TotalMs}",
                MaxRetryAttempts,
                totalStopwatch.ElapsedMilliseconds);

            return Array.Empty<MixAiRecommendation>();
        }

        private static string BuildCacheKey(string question, int maxResults) =>
            "recommend:" + question.Trim().ToLowerInvariant() + ":" + maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
