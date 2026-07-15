using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>Lists playlist paths from a stored upload (resolving the literal id <c>latest</c>).</summary>
    public interface IListUploadPlaylistsUseCase
    {
        Task<ListUploadPlaylistsResult> ListAsync(string uploadId, CancellationToken cancellationToken);
    }
}
