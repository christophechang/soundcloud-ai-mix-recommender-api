using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Configuration
{
    /// <summary>
    /// Startup validator for <see cref="MixLabStorageOptions"/>, mirroring
    /// <see cref="BlobCatalogOptionsValidator"/> so a misconfigured deploy fails fast instead of
    /// returning 500 on the first MixLab request.
    /// </summary>
    internal sealed class MixLabStorageOptionsValidator : IValidateOptions<MixLabStorageOptions>
    {
        public ValidateOptionsResult Validate(string? name, MixLabStorageOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.ContainerName))
            {
                failures.Add("Azure:MixLab:ContainerName is not configured.");
            }

            bool hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
            bool hasServiceEndpoint = !string.IsNullOrWhiteSpace(options.ServiceEndpoint);

            if (!hasConnectionString && !hasServiceEndpoint)
            {
                failures.Add(
                    "Either Azure:MixLab:ConnectionString or Azure:MixLab:ServiceEndpoint must be configured.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
