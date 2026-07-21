using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Stand-in for environments running without AI mood enrichment. Resolves no weights, so a
    /// mood the configuration does not cover simply keeps no weight for that cycle.
    /// </summary>
    internal sealed class NullMoodWeightEnricher : IMoodWeightEnricher
    {
        public static NullMoodWeightEnricher Instance { get; } = new NullMoodWeightEnricher();

        public Task<IReadOnlyDictionary<string, double>> EnrichAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
    }
}
