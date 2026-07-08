using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// The mutable run manifest document, <c>runs/{runId}/run.json</c> — the only per-run mutable
    /// blob; every other run artifact is written once. See docs/architecture/mixlab-anywhere.md §5.1.
    /// </summary>
    public sealed record MixLabRun
    {
        public int SchemaVersion { get; init; } = 1;

        required public string RunId { get; init; }

        required public DateTimeOffset CreatedAt { get; init; }

        required public MixLabRunStatus Status { get; init; }

        required public MixLabRunFlags Flags { get; init; }

        required public string UploadId { get; init; }

        public DateTimeOffset? ClaimedAt { get; init; }

        public string? WorkerId { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public string? Error { get; init; }

        public IReadOnlyList<MixLabRunConcept> Concepts { get; init; } = Array.Empty<MixLabRunConcept>();
    }
}
