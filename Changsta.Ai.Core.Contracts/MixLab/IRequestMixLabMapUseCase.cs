using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Requests a library-map job for an upload: resolves the target upload (a concrete id or the
    /// literal <c>latest</c>) to a concrete id at request time, and enqueues (or refreshes) a
    /// <c>queued</c> map job. Backs <c>POST /api/mixlab/maps</c>.
    /// </summary>
    public interface IRequestMixLabMapUseCase
    {
        /// <summary>
        /// Resolves <paramref name="uploadId"/> and, when valid, requests a map job. The caller
        /// maps <see cref="RequestMixLabMapResult.Outcome"/> to a transport status code.
        /// </summary>
        Task<RequestMixLabMapResult> RequestAsync(string uploadId, CancellationToken cancellationToken);
    }
}
