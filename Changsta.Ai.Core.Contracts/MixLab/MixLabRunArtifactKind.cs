namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// The three immutable per-run artifacts that can be streamed back to a client. The use case
    /// maps each to its blob name and content type. See docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    public enum MixLabRunArtifactKind
    {
        /// <summary><c>report.html</c>, served as <c>text/html</c>.</summary>
        Report,

        /// <summary><c>export.xml</c> (Rekordbox playlist), served as <c>application/xml</c>. Optional per run.</summary>
        Export,

        /// <summary><c>summary.json</c>, served as <c>application/json</c>.</summary>
        Summary,
    }
}
