using System;

namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// A library-map job entry, <c>maps/index.json</c> — tracks the request/claim/complete
    /// lifecycle for a per-upload library-map. The engine payload itself is stored separately and
    /// verbatim at <c>maps/{uploadId}.json</c>; the API never deserialises it.
    /// </summary>
    public sealed record MixLabMapJob
    {
        public int SchemaVersion { get; init; } = 1;

        required public string UploadId { get; init; }

        required public MixLabMapStatus Status { get; init; }

        required public DateTimeOffset RequestedAt { get; init; }

        public DateTimeOffset? ClaimedAt { get; init; }

        public string? WorkerId { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public string? Error { get; init; }
    }
}
