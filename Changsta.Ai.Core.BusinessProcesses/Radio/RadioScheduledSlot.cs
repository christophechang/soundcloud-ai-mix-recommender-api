using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    internal sealed class RadioScheduledSlot
    {
        required internal int Hour { get; init; }

        required internal Mix Mix { get; init; }

        required internal RadioSlotScore Score { get; init; }

        internal IReadOnlyList<string> AuditReasons { get; init; } = System.Array.Empty<string>();

        internal IReadOnlyList<string> AuditWarnings { get; init; } = System.Array.Empty<string>();

        internal IReadOnlyList<string> RelaxedRules { get; init; } = System.Array.Empty<string>();
    }
}
