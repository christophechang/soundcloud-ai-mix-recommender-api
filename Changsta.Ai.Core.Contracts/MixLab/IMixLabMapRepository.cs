using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Persistence for MixLab library-map jobs (<c>maps/index.json</c>) and their engine payloads
    /// (<c>maps/{uploadId}.json</c>).
    /// </summary>
    public interface IMixLabMapRepository
    {
        Task<MixLabMapJob> RequestAsync(string uploadId, CancellationToken cancellationToken);

        /// <summary>
        /// Atomically claims the oldest queued map job, requeuing any stale claims (running with a
        /// <c>claimedAt</c> older than <paramref name="staleLease"/>) first. Returns
        /// <see langword="null"/> when no job is queued.
        /// </summary>
        Task<MixLabMapJob?> TryClaimOldestQueuedAsync(string workerId, TimeSpan staleLease, CancellationToken cancellationToken);

        /// <summary>Stores <paramref name="payload"/> verbatim and marks the job succeeded.</summary>
        Task CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

        Task FailAsync(string uploadId, string error, CancellationToken cancellationToken);

        Task<MixLabMapJob?> GetJobAsync(string uploadId, CancellationToken cancellationToken);

        /// <summary>Returns the stored engine payload bytes, or <see langword="null"/> when absent.</summary>
        Task<byte[]?> OpenPayloadAsync(string uploadId, CancellationToken cancellationToken);
    }
}
