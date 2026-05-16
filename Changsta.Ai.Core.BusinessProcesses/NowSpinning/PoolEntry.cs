using System.Collections.Generic;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal sealed class PoolEntry
    {
        internal PoolEntry(Mix mix, IReadOnlySet<MoodLean> leanTags)
        {
            Mix = mix;
            LeanTags = leanTags;
        }

        internal Mix Mix { get; }

        internal IReadOnlySet<MoodLean> LeanTags { get; }
    }
}
