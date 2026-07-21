using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Owns everything to do with mood weights. Both enrichment dependencies are required here —
    /// callers that run without AI enrichment pass <see cref="NullMoodWeightEnricher"/> and
    /// <see cref="NullMoodWeightEnrichmentRepository"/> rather than nulls, so no code path has to
    /// re-check whether enrichment is configured.
    /// </summary>
    internal sealed class MoodWeightResolver : IMoodWeightResolver
    {
        private readonly IReadOnlyDictionary<string, double> _baseWeights;
        private readonly IMoodWeightEnrichmentRepository _enrichmentRepository;
        private readonly IMoodWeightEnricher _enricher;
        private readonly ILogger _logger;

        public MoodWeightResolver(
            IReadOnlyDictionary<string, double> baseWeights,
            IMoodWeightEnrichmentRepository enrichmentRepository,
            IMoodWeightEnricher enricher,
            ILogger logger)
        {
            _baseWeights = baseWeights ?? throw new ArgumentNullException(nameof(baseWeights));
            _enrichmentRepository = enrichmentRepository ?? throw new ArgumentNullException(nameof(enrichmentRepository));
            _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyDictionary<string, double>> ResolveAsync(
            IReadOnlyList<Mix> mixes,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, double> enrichedWeights =
                await LoadEnrichedWeightsSafeAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyDictionary<string, double> effectiveWeights = MergeWeights(_baseWeights, enrichedWeights);

            IReadOnlyList<string> unknownMoods = FindUnknownMoods(mixes, effectiveWeights);

            if (unknownMoods.Count == 0)
            {
                return effectiveWeights;
            }

            _logger.LogInformation(
                "Enriching {Count} unknown mood(s) via AI: {Moods}",
                unknownMoods.Count,
                string.Join(", ", unknownMoods));

            IReadOnlyDictionary<string, double> newScores =
                await EnrichMoodsSafeAsync(effectiveWeights, unknownMoods, cancellationToken).ConfigureAwait(false);

            if (newScores.Count == 0)
            {
                return effectiveWeights;
            }

            enrichedWeights = MergeWeights(enrichedWeights, newScores);
            effectiveWeights = MergeWeights(_baseWeights, enrichedWeights);

            await PersistSafeAsync(enrichedWeights, cancellationToken).ConfigureAwait(false);

            return effectiveWeights;
        }

        internal static IReadOnlyDictionary<string, double> MergeWeights(
            IReadOnlyDictionary<string, double> baseWeights,
            IReadOnlyDictionary<string, double> additions)
        {
            var merged = new Dictionary<string, double>(baseWeights, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in additions)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        internal static IReadOnlyList<string> FindUnknownMoods(
            IReadOnlyList<Mix> mixes,
            IReadOnlyDictionary<string, double> weights)
        {
            return mixes
                .SelectMany(m => m.Moods)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !weights.ContainsKey(m))
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<IReadOnlyDictionary<string, double>> LoadEnrichedWeightsSafeAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await _enrichmentRepository.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.EnrichedWeightsLoadFailures.Add(1);
                _logger.LogWarning(ex, "Failed to load enriched mood weights — proceeding with base weights only.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task<IReadOnlyDictionary<string, double>> EnrichMoodsSafeAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> unknownMoods,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _enricher
                    .EnrichAsync(existingWeights, unknownMoods, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CatalogueMetrics.AiEnrichmentFailures.Add(1);
                _logger.LogWarning(ex, "AI mood enrichment failed — new moods will have no weight this cycle.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task PersistSafeAsync(
            IReadOnlyDictionary<string, double> enrichedWeights,
            CancellationToken cancellationToken)
        {
            try
            {
                await _enrichmentRepository.WriteAsync(enrichedWeights, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to persist enriched mood weights — continuing with in-memory scores for this catalog load.");
            }
        }
    }
}
