using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal interface ICataloguePersister
    {
        Task PersistIfChangedAsync(
            IReadOnlyList<Mix> merged,
            CatalogueLoadResult load,
            bool derivedFieldsChanged,
            CancellationToken cancellationToken);
    }
}
