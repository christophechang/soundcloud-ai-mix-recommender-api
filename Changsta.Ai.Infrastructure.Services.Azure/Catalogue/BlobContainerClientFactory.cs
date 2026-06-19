using System;
using Azure.Core;
using Azure.Storage.Blobs;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Builds the long-lived <see cref="BlobContainerClient"/> used by the blob repositories. In
    /// production it targets the service endpoint with managed identity; for local development it
    /// uses a connection string. Centralises the previously duplicated construction so both
    /// repositories build the client the same way. See issue #44.
    /// </summary>
    internal static class BlobContainerClientFactory
    {
        public static BlobContainerClient Create(BlobCatalogOptions options, TokenCredential credential)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(credential);

            if (!string.IsNullOrWhiteSpace(options.ServiceEndpoint))
            {
                var containerUri = new Uri(
                    options.ServiceEndpoint!.TrimEnd('/') + "/" + options.ContainerName);
                return new BlobContainerClient(containerUri, credential);
            }

            return new BlobContainerClient(options.ConnectionString, options.ContainerName);
        }
    }
}
