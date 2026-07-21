using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal interface ICatalogueHydrator
    {
        Task<CatalogueHydrationResult> HydrateAsync(
            IReadOnlyList<Mix> merged,
            CancellationToken cancellationToken);
    }
}
