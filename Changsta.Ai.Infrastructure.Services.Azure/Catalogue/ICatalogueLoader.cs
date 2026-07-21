using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal interface ICatalogueLoader
    {
        Task<CatalogueLoadResult> LoadAsync(CancellationToken cancellationToken);
    }
}
