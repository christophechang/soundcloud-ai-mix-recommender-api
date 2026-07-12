using System;

namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// An uploaded, gzipped Rekordbox collection XML. Doubles as the <c>uploads/index.json</c>
    /// entry shape (no separate index-entry type — see docs/architecture/mixlab-anywhere.md §3).
    /// </summary>
    public sealed record MixLabUpload
    {
        required public string UploadId { get; init; }

        required public DateTimeOffset UploadedAt { get; init; }

        required public long SizeBytes { get; init; }

        public string? Label { get; init; }
    }
}
