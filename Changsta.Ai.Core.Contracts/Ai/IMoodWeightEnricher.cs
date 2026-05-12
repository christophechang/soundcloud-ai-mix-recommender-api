using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.Ai
{
    public interface IMoodWeightEnricher
    {
        Task<IReadOnlyDictionary<string, double>> EnrichAsync(
            IReadOnlyDictionary<string, double> existingWeights,
            IReadOnlyList<string> newMoods,
            CancellationToken cancellationToken = default);
    }
}
