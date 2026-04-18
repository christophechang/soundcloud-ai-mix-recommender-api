using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface ICatalogFlushUseCase
    {
        Task FlushAsync(CancellationToken cancellationToken);
    }
}
