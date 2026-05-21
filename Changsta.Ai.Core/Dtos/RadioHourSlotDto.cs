using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class RadioHourSlotDto
    {
        required public int Hour { get; init; }

        required public Mix Mix { get; init; }

        public bool IsCurrent { get; init; }

        public IReadOnlyList<string> AuditWarnings { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> RelaxedRules { get; init; } = Array.Empty<string>();
    }
}
