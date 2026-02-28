namespace Changsta.Ai.Infrastructure.Services.Azure.Configuration
{
    public sealed class BlobCatalogOptions
    {
        required public string ConnectionString { get; init; }

        required public string ContainerName { get; init; }

        required public string BlobName { get; init; }
    }
}
