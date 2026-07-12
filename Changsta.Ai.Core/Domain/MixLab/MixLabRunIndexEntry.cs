using System;

namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// A <c>runs/index.json</c> entry — the archive list projection of a run manifest, newest
    /// first. See docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    public sealed record MixLabRunIndexEntry
    {
        required public string RunId { get; init; }

        required public DateTimeOffset CreatedAt { get; init; }

        required public MixLabRunStatus Status { get; init; }

        required public string Genre { get; init; }

        required public string FlagsSummary { get; init; }

        required public int ConceptCount { get; init; }
    }
}
