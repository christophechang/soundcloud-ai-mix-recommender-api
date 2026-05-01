using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface IDeleteMixUseCase
    {
        Task<bool> DeleteAsync(string slug, CancellationToken cancellationToken);
    }
}
