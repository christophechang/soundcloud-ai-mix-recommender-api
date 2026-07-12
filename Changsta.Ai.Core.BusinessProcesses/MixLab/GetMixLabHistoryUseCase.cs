using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Returns <c>history/concept-history.json</c> verbatim from <see cref="IMixLabHistoryStore"/>.
    /// See docs/architecture/mixlab-anywhere.md §4 row 13 and issue #131.
    /// </summary>
    public sealed class GetMixLabHistoryUseCase : IGetMixLabHistoryUseCase
    {
        private readonly IMixLabHistoryStore _history;

        public GetMixLabHistoryUseCase(IMixLabHistoryStore history)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public Task<MixLabHistorySnapshot?> GetAsync(CancellationToken cancellationToken) =>
            _history.GetAsync(cancellationToken);
    }
}
