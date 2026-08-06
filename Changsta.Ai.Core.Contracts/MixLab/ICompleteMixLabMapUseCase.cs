using System;
using System.Threading;
using System.Threading.Tasks;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Completes a running map job: stores the engine payload verbatim and flips the job to
    /// <c>succeeded</c>. Backs <c>POST /api/mixlab/maps/{uploadId}/result</c>.
    /// </summary>
    public interface ICompleteMixLabMapUseCase
    {
        /// <summary>
        /// Stores <paramref name="payload"/> for the job and marks it succeeded. Returns
        /// <see langword="false"/> when no job exists for <paramref name="uploadId"/> or it is not
        /// currently running (the controller then replies 404).
        /// </summary>
        Task<bool> CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    }
}
