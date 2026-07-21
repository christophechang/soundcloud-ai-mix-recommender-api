using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal interface IMoodWeightResolver
    {
        /// <summary>
        /// Returns the mood weights to score with: the configured base weights, overlaid with
        /// previously enriched weights, overlaid with anything the AI could resolve for moods
        /// still lacking a weight.
        /// </summary>
        Task<IReadOnlyDictionary<string, double>> ResolveAsync(
            IReadOnlyList<Mix> mixes,
            CancellationToken cancellationToken);
    }
}
