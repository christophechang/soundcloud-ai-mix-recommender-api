using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Changsta.Ai.Infrastructure.Services.Azure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobMixCatalogueRepository : IBlobMixCatalogueRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private readonly BlobContainerClient _containerClient;
        private readonly string _blobName;
        private readonly ILogger<BlobMixCatalogueRepository> _logger;

        public BlobMixCatalogueRepository(
            IOptions<BlobCatalogOptions> options,
            ILogger<BlobMixCatalogueRepository> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(resolved.ConnectionString))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:ConnectionString is not configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.ContainerName))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:ContainerName is not configured.");
            }

            if (string.IsNullOrWhiteSpace(resolved.BlobName))
            {
                throw new InvalidOperationException("Azure:BlobCatalog:BlobName is not configured.");
            }

            _containerClient = new BlobContainerClient(resolved.ConnectionString, resolved.ContainerName);
            _blobName = resolved.BlobName;
        }

        public async Task<IReadOnlyList<Mix>> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var blobClient = _containerClient.GetBlobClient(_blobName);
                var download = await blobClient
                    .DownloadContentAsync(cancellationToken)
                    .ConfigureAwait(false);

                var document = JsonSerializer.Deserialize<MixCatalogDocument>(
                    download.Value.Content.ToStream(),
                    JsonOptions);

                return document?.Mixes ?? Array.Empty<Mix>();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Blob catalog not found — starting with empty catalog on first run.");
                return Array.Empty<Mix>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read blob catalog — treating as empty.");
                return Array.Empty<Mix>();
            }
        }

        public async Task WriteAsync(IReadOnlyList<Mix> mixes, CancellationToken cancellationToken)
        {
            try
            {
                await _containerClient
                    .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var document = new MixCatalogDocument
                {
                    SchemaVersion = 1,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                    Mixes = mixes,
                };

                using var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                stream.Position = 0;

                var blobClient = _containerClient.GetBlobClient(_blobName);
                await blobClient
                    .UploadAsync(stream, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write blob catalog — merged result was still returned to caller.");
            }
        }
    }
}
