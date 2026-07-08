namespace Changsta.Ai.Core.Domain.MixLab
{
    /// <summary>
    /// The CLI-flag levers captured when a run is enqueued (<c>POST /api/mixlab/runs</c>) and
    /// persisted verbatim into the run manifest so the worker can rebuild the <c>./mixlab</c>
    /// argv. See docs/architecture/mixlab-anywhere.md §5.1 and §6. Allow-list validation of the
    /// values (e.g. known genres/modes) is an API-layer concern, not enforced by this record.
    /// </summary>
    public sealed record MixLabRunFlags
    {
        required public string Genre { get; init; }

        required public string Mode { get; init; }

        required public string Risk { get; init; }

        required public string Directions { get; init; }

        public string? Intent { get; init; }

        public int? MixLength { get; init; }

        public bool Resequence { get; init; }

        public bool Deep { get; init; }

        public int? Stage1Seed { get; init; }
    }
}
