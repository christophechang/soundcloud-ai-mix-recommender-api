namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// The result of reading <c>history/concept-history.json</c>: the raw content (opaque to the
    /// API — it mirrors the Python engine's <c>.mixlab/concept-history.json</c> schema) plus the
    /// storage ETag captured at read time, passed back to <see cref="IMixLabHistoryStore.PutAsync"/>
    /// for optimistic concurrency (If-Match). See docs/architecture/mixlab-anywhere.md §3 and §4
    /// row 13.
    /// </summary>
    public sealed class MixLabHistorySnapshot
    {
        public MixLabHistorySnapshot(string content, string eTag)
        {
            Content = content;
            ETag = eTag;
        }

        public string Content { get; }

        public string ETag { get; }
    }
}
