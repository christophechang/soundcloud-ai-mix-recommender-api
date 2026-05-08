using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Diagnostics
{
    public sealed class DiagnosticsResult
    {
        required public DateTimeOffset GeneratedAt { get; init; }

        required public int WindowHours { get; init; }

        public IReadOnlyList<DiagnosticsRequest> Requests { get; init; } = Array.Empty<DiagnosticsRequest>();

        public IReadOnlyList<DiagnosticsException> Exceptions { get; init; } = Array.Empty<DiagnosticsException>();
    }
}
