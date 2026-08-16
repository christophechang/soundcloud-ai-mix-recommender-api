using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Thin pass-through to <see cref="IMixLabRunRepository.RecomputeIndexCountsAsync"/>: the work
    /// is inherently a persistence-layer sweep, but the controller talks to use cases, never to
    /// repositories.
    /// </summary>
    public sealed class ReindexMixLabRunsUseCase : IReindexMixLabRunsUseCase
    {
        private readonly IMixLabRunRepository _runs;

        public ReindexMixLabRunsUseCase(IMixLabRunRepository runs)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        }

        public Task<int> ReindexAsync(CancellationToken cancellationToken)
        {
            return _runs.RecomputeIndexCountsAsync(cancellationToken);
        }
    }
}
