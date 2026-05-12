using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobMoodWeightEnrichmentRepository : IMoodWeightEnrichmentRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private readonly BlobContainerClient _containerClient;
        private readonly string _blobName;
        private readonly ILogger<BlobMoodWeightEnrichmentRepository> _logger;

        public BlobMoodWeightEnrichmentRepository(
            IOptions<BlobCatalogOptions> options,
            ILogger<BlobMoodWeightEnrichmentRepository> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            bool hasConnectionString = !string.IsNullOrWhiteSpace(resolved.ConnectionString);
            bool hasServiceEndpoint = !string.IsNullOrWhiteSpace(resolved.ServiceEndpoint);

            if (!hasConnectionString && !hasServiceEndpoint)
            {
                throw new InvalidOperationException(
                    "Either Azure:BlobCatalog:ConnectionString or Azure:BlobCatalog:ServiceEndpoint must be configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.ContainerName))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:ContainerName is not configured.");
            }

            if (hasServiceEndpoint)
            {
                var containerUri = new Uri(
                    resolved.ServiceEndpoint!.TrimEnd('/') + "/" + resolved.ContainerName);
                _containerClient = new BlobContainerClient(containerUri, new DefaultAzureCredential());
            }
            else
            {
                _containerClient = new BlobContainerClient(resolved.ConnectionString, resolved.ContainerName);
            }

            _blobName = resolved.EnrichedMoodWeightsBlobName;
        }

        public async Task<IReadOnlyDictionary<string, double>> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var blobClient = _containerClient.GetBlobClient(_blobName);
                var download = await blobClient
                    .DownloadContentAsync(cancellationToken)
                    .ConfigureAwait(false);

                var dict = JsonSerializer.Deserialize<Dictionary<string, double>>(
                    download.Value.Content.ToStream(), JsonOptions)
                    ?? new Dictionary<string, double>();

                return new Dictionary<string, double>(dict, StringComparer.OrdinalIgnoreCase);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Enriched mood weights blob not found — starting with empty enrichment.");
                return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task WriteAsync(IReadOnlyDictionary<string, double> weights, CancellationToken cancellationToken)
        {
            try
            {
                await _containerClient
                    .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                using var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, weights, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                stream.Position = 0;

                var blobClient = _containerClient.GetBlobClient(_blobName);
                await blobClient
                    .UploadAsync(stream, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write enriched mood weights blob.");
            }
        }
    }
}
