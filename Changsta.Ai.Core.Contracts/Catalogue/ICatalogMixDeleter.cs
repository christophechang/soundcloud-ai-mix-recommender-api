using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface ICatalogMixDeleter
    {
        Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken);
    }
}
