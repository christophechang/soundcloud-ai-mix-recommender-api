namespace Changsta.Ai.Infrastructure.Services.Azure.Configuration
{
    public sealed class BlobCatalogOptions
    {
        /// <summary>Used for local development (Azurite or real connection string). Leave empty in production.</summary>
        public string? ConnectionString { get; init; }

        /// <summary>Blob service endpoint URL (e.g. https://account.blob.core.windows.net). Used in production with Managed Identity.</summary>
        public string? ServiceEndpoint { get; init; }

        required public string ContainerName { get; init; }

        required public string BlobName { get; init; }

        public string EnrichedMoodWeightsBlobName { get; init; } = "mood_weights_enriched.json";
    }
}
