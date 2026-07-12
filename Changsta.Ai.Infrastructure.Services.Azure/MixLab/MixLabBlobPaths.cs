namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Blob path layout for the <c>mixlab</c> container. See
    /// docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    internal static class MixLabBlobPaths
    {
        public const string UploadsIndex = "uploads/index.json";

        public const string RunsIndex = "runs/index.json";

        public const string HistoryDocument = "history/concept-history.json";

        public const string FeedbackPending = "feedback/pending.json";

        public static string UploadContent(string uploadId) => $"uploads/{uploadId}.xml.gz";

        public static string RunManifest(string runId) => $"runs/{runId}/run.json";

        public static string RunArtifact(string runId, string name) => $"runs/{runId}/{name}";
    }
}
