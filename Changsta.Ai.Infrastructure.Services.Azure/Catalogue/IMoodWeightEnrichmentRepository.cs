using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    public interface IMoodWeightEnrichmentRepository
    {
        Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken);

        Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken);
    }
}
