using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Fails a running map job, storing the error. Backs <c>POST /api/mixlab/maps/{uploadId}/fail</c>.
    /// </summary>
    public interface IFailMixLabMapUseCase
    {
        /// <summary>
        /// Marks the job failed with <paramref name="error"/>. Returns <see langword="false"/> when
        /// no job exists for <paramref name="uploadId"/> or it is not currently running (the
        /// controller then replies 404).
        /// </summary>
        Task<bool> FailAsync(string uploadId, string error, CancellationToken cancellationToken);
    }
}
