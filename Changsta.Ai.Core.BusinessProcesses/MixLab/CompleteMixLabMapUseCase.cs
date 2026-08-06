using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Enforces the running-state gate the repository does not: only a job the caller finds
    /// <see cref="MixLabMapStatus.Running"/> may be completed, guarding against a rogue or
    /// late-arriving worker resurrecting a job that has since been requeued or refreshed. See
    /// issue #128.
    /// </summary>
    public sealed class CompleteMixLabMapUseCase : ICompleteMixLabMapUseCase
    {
        private readonly IMixLabMapRepository _maps;

        public CompleteMixLabMapUseCase(IMixLabMapRepository maps)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        }

        public async Task<bool> CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            MixLabMapJob? job = await _maps.GetJobAsync(uploadId, cancellationToken).ConfigureAwait(false);
            if (job is null || job.Status != MixLabMapStatus.Running)
            {
                return false;
            }

            await _maps.CompleteAsync(uploadId, payload, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }
}
