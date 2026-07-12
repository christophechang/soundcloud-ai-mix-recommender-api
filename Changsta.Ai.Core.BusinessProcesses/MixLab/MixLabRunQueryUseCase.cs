using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Read-side queries over the run index and manifests, normalising paging inputs before
    /// delegating to the repository. See issue #130.
    /// </summary>
    public sealed class MixLabRunQueryUseCase : IMixLabRunQueryUseCase
    {
        private const int DefaultTake = 20;

        private const int MaxTake = 100;

        private readonly IMixLabRunRepository _runs;

        public MixLabRunQueryUseCase(IMixLabRunRepository runs)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        }

        public Task<IReadOnlyList<MixLabRunIndexEntry>> ListAsync(int? take, int? skip, CancellationToken cancellationToken)
        {
            int effectiveTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
            int effectiveSkip = Math.Max(skip ?? 0, 0);
            return _runs.GetIndexAsync(effectiveTake, effectiveSkip, cancellationToken);
        }

        public Task<MixLabRun?> GetAsync(string runId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            return _runs.GetAsync(runId, cancellationToken);
        }
    }
}
