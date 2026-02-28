using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    public interface IBlobMixCatalogueRepository
    {
        Task<IReadOnlyList<Mix>> ReadAsync(CancellationToken cancellationToken);

        Task WriteAsync(IReadOnlyList<Mix> mixes, CancellationToken cancellationToken);
    }
}
