using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Ai;
using Changsta.Ai.Core.Contracts.Catalogue;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Stand-in for environments with no enrichment store. Reads empty and discards writes, so the
    /// resolver never has to branch on whether persistence is configured.
    /// </summary>
    internal sealed class NullMoodWeightEnrichmentRepository : IMoodWeightEnrichmentRepository
    {
        public static NullMoodWeightEnrichmentRepository Instance { get; } = new NullMoodWeightEnrichmentRepository();

        public Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

        public Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
