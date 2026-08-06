using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Delegates to the repository's atomic claim, supplying the configured stale-lease window.
    /// The repository requeues stale running jobs before claiming the oldest queued one, mirroring
    /// <see cref="ClaimMixLabRunUseCase"/>. See issue #128.
    /// </summary>
    public sealed class ClaimMixLabMapUseCase : IClaimMixLabMapUseCase
    {
        private readonly IMixLabMapRepository _maps;
        private readonly MixLabOptions _options;

        public ClaimMixLabMapUseCase(IMixLabMapRepository maps, MixLabOptions options)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Task<MixLabMapJob?> ClaimAsync(string workerId, CancellationToken cancellationToken)
        {
            TimeSpan staleLease = TimeSpan.FromMinutes(_options.ClaimLeaseMinutes);
            return _maps.TryClaimOldestQueuedAsync(workerId, staleLease, cancellationToken);
        }
    }
}
