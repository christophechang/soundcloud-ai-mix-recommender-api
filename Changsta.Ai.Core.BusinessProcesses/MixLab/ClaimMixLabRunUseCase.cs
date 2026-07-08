using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Delegates to the repository's atomic claim, supplying the configured stale-lease window.
    /// The repository requeues stale running runs before claiming the oldest queued one. See
    /// issue #130.
    /// </summary>
    public sealed class ClaimMixLabRunUseCase : IClaimMixLabRunUseCase
    {
        private readonly IMixLabRunRepository _runs;
        private readonly MixLabOptions _options;

        public ClaimMixLabRunUseCase(IMixLabRunRepository runs, MixLabOptions options)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Task<MixLabRun?> ClaimAsync(string workerId, CancellationToken cancellationToken)
        {
            TimeSpan staleLease = TimeSpan.FromMinutes(_options.ClaimLeaseMinutes);
            return _runs.TryClaimOldestQueuedAsync(workerId, staleLease, cancellationToken);
        }
    }
}
