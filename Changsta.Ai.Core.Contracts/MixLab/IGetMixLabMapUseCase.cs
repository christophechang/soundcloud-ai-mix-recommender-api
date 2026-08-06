using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Reads a map job and, when succeeded, its engine payload. Backs
    /// <c>GET /api/mixlab/maps/{uploadId}</c>.
    /// </summary>
    public interface IGetMixLabMapUseCase
    {
        /// <summary>
        /// Resolves <paramref name="uploadId"/> (a concrete id or the literal <c>latest</c>) and
        /// returns its map job, or <see cref="GetMixLabMapResult.GetOutcome.NotFound"/> when unknown.
        /// </summary>
        Task<GetMixLabMapResult> GetAsync(string uploadId, CancellationToken cancellationToken);
    }
}
