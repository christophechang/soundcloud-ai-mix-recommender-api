using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal sealed class NowSpinningPools
    {
        required public IReadOnlyDictionary<(SlotKey, DayBucket), IReadOnlyList<PoolEntry>> Pools { get; init; }
    }
}
