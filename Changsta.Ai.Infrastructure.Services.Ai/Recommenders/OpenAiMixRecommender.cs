using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
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

        // Bump whenever MixPromptBuilder or MixRecommendationQueryAnalyzer change in a way
        // that should invalidate previously-cached AI responses for the same question.
        private const int PromptVersion = 1;

        private readonly ChatClient _chat;
        private readonly IMemoryCache _cache;
        private readonly ICatalogCacheInvalidator _catalogVersion;
        private readonly ILogger<OpenAiMixRecommender> _logger;
        private readonly int _timeoutSeconds;

        public OpenAiMixRecommender(
            IOptions<OpenAiOptions> options,
            IMemoryCache cache,
            ICatalogCacheInvalidator catalogVersion,
            ILogger<OpenAiMixRecommender> logger)
        {
            var resolvedOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _catalogVersion = catalogVersion ?? throw new ArgumentNullException(nameof(catalogVersion));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // ApiKey / Model / TimeoutSeconds are validated at startup by OpenAiOptionsValidator
            // (AddOptions<OpenAiOptions>().Validate(...).ValidateOnStart()), so no constructor-time
            // re-validation is needed here. See issue #40.
            _timeoutSeconds = resolvedOptions.TimeoutSeconds;
            _chat = OpenAiChatClientFactory.Create(resolvedOptions);
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

            string cacheKey = BuildCacheKey(question, maxResults, PromptVersion, _catalogVersion.Version);
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MixAiRecommendation>? cached) && cached != null)
            {
                totalStopwatch.Stop();
                _logger.LogInformation(
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

            _logger.LogInformation(
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
                    ChatCompletion completion = await _chat
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
                        _cache.Set(cacheKey, (IReadOnlyList<MixAiRecommendation>)results, TimeSpan.FromMinutes(RecommendationCacheTtlMinutes));
                    }

                    attemptStopwatch.Stop();
                    totalStopwatch.Stop();
                    _logger.LogInformation(
                        "Recommendation request succeeded. attempt={Attempt} resultCount={ResultCount} openAiMs={OpenAiMs} totalMs={TotalMs}",
                        attempt,
                        results.Length,
                        attemptStopwatch.ElapsedMilliseconds,
                        totalStopwatch.ElapsedMilliseconds);
                    return results;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The caller (request) was cancelled — propagate so the middleware can short-circuit.
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    // Our configured network timeout fired (not a caller cancellation). Surface a
                    // clean, handled failure rather than retrying — retrying a timeout only doubles
                    // the stall — and rather than returning an empty 200 that reads like "no matches".
                    attemptStopwatch.Stop();
                    totalStopwatch.Stop();
                    _logger.LogWarning(
                        ex,
                        "OpenAI recommendation request timed out after {TimeoutSeconds}s. attempt={Attempt}",
                        _timeoutSeconds,
                        attempt);
                    throw new TimeoutException(
                        $"OpenAI request timed out after {_timeoutSeconds}s.", ex);
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException or HttpRequestException)
                {
                    attemptStopwatch.Stop();
                    lastException = ex;
                    _logger.LogInformation(
                        "Recommendation attempt failed. attempt={Attempt} maxAttempts={MaxAttempts} openAiMs={OpenAiMs} failureType={FailureType} message={FailureMessage}",
                        attempt,
                        MaxRetryAttempts,
                        attemptStopwatch.ElapsedMilliseconds,
                        ex.GetType().Name,
                        ex.Message);
                    _logger.LogWarning(
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

            _logger.LogError(
                lastException,
                "All {MaxAttempts} AI recommendation attempts failed. Returning empty results.",
                MaxRetryAttempts);
            totalStopwatch.Stop();
            _logger.LogInformation(
                "Recommendation request failed after retries. maxAttempts={MaxAttempts} totalMs={TotalMs}",
                MaxRetryAttempts,
                totalStopwatch.ElapsedMilliseconds);

            return Array.Empty<MixAiRecommendation>();
        }

        internal static string BuildCacheKey(string question, int maxResults, int promptVersion, int catalogueVersion) =>
            "recommend:v" + promptVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":c" + catalogueVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + question.Trim().ToLowerInvariant()
            + ":" + maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
