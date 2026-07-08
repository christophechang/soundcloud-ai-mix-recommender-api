using System;
using Azure.Core;
using Azure.Storage.Blobs;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Builds the long-lived <see cref="BlobContainerClient"/> for the MixLab container, the same
    /// way <see cref="Changsta.Ai.Infrastructure.Services.Azure.Catalogue.BlobContainerClientFactory"/>
    /// does for the mix catalogue. Deliberately duplicated rather than generalising the shared
    /// factory to a generic options shape, to keep this ticket's diff scoped to the MixLab area —
    /// see issue #128.
    /// </summary>
    internal static class MixLabBlobContainerClientFactory
    {
        public static BlobContainerClient Create(MixLabStorageOptions options, TokenCredential credential)
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
