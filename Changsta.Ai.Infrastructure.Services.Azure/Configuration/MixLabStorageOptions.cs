namespace Changsta.Ai.Infrastructure.Services.Azure.Configuration
{
    /// <summary>
    /// Binds to <c>Azure:MixLab</c>. Follows the same connection-string-or-service-endpoint shape
    /// as <see cref="BlobCatalogOptions"/>, but for the dedicated <c>mixlab</c> container described
    /// in docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    public sealed class MixLabStorageOptions
    {
        /// <summary>Used for local development (Azurite or real connection string). Leave empty in production.</summary>
        public string? ConnectionString { get; init; }

        /// <summary>Blob service endpoint URL (e.g. https://account.blob.core.windows.net). Used in production with Managed Identity.</summary>
        public string? ServiceEndpoint { get; init; }

        public string ContainerName { get; init; } = "mixlab";
    }
}
