using System;

namespace Changsta.Ai.Core.Diagnostics
{
    public sealed class DiagnosticsRequest
    {
        required public DateTimeOffset Timestamp { get; init; }

        required public int StatusCode { get; init; }

        required public string Name { get; init; }

        public string? Url { get; init; }

        public double DurationMs { get; init; }

        public string? OperationId { get; init; }
    }
}
