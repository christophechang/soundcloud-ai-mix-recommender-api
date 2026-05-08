using System;

namespace Changsta.Ai.Core.Diagnostics
{
    public sealed class DiagnosticsException
    {
        required public DateTimeOffset Timestamp { get; init; }

        public string? Type { get; init; }

        public string? Message { get; init; }

        public string? OperationId { get; init; }
    }
}
