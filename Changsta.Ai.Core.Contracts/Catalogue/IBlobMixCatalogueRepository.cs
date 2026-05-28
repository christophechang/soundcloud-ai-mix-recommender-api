using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface IBlobMixCatalogueRepository
    {
        Task<IReadOnlyList<Mix>> ReadAsync(CancellationToken cancellationToken);

        Task WriteAsync(IReadOnlyList<Mix> mixes, CancellationToken cancellationToken);
    }
}
