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

        /// <summary>
        /// How many of this run's concepts carry <see cref="MixLabRunConcept.Shortlisted"/>. A
        /// derived projection of the manifest, refreshed on every shortlist/feedback write and
        /// repairable via <c>POST /api/mixlab/runs/reindex</c>. Not <c>required</c>: entries written
        /// before this field existed omit it and must read as 0.
        /// </summary>
        public int ShortlistedCount { get; init; }

        /// <summary>
        /// How many of this run's concepts have feedback whose verdict is
        /// <see cref="MixLabFeedbackVerdict.Played"/> or
        /// <see cref="MixLabFeedbackVerdict.PlayedModified"/>. Same derivation, staleness and
        /// legacy-default rules as <see cref="ShortlistedCount"/>.
        /// </summary>
        public int PlayedCount { get; init; }
    }
}
