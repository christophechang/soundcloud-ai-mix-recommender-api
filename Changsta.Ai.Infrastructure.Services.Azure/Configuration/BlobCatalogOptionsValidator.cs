using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Configuration
{
    /// <summary>
    /// Startup validator for <see cref="BlobCatalogOptions"/>. Asserts the per-field requirements
    /// and the cross-field "ConnectionString OR ServiceEndpoint" rule so the host fails fast on a
    /// misconfigured deploy instead of returning 500 on the first request that hits blob storage.
    /// </summary>
    internal sealed class BlobCatalogOptionsValidator : IValidateOptions<BlobCatalogOptions>
    {
        public ValidateOptionsResult Validate(string? name, BlobCatalogOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.ContainerName))
            {
                failures.Add("Azure:BlobCatalog:ContainerName is not configured.");
            }

            if (string.IsNullOrWhiteSpace(options.BlobName))
            {
                failures.Add("Azure:BlobCatalog:BlobName is not configured.");
            }

            bool hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
            bool hasServiceEndpoint = !string.IsNullOrWhiteSpace(options.ServiceEndpoint);

            if (!hasConnectionString && !hasServiceEndpoint)
            {
                failures.Add(
                    "Either Azure:BlobCatalog:ConnectionString or Azure:BlobCatalog:ServiceEndpoint must be configured.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
