using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Azure Blob Storage-backed <see cref="IMixLabBlobGateway"/>. Mirrors the read/write
    /// conditioning pattern used by <see cref="Changsta.Ai.Infrastructure.Services.Azure.Catalogue.BlobMixCatalogueRepository"/>.
    /// Untested directly (same convention as that class); the read-modify-write, claim, and
    /// idempotency logic that sits above this gateway is unit tested via the in-memory
    /// FakeMixLabBlobGateway test double. See issue #128.
    /// </summary>
    internal sealed class MixLabBlobGateway : IMixLabBlobGateway
    {
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<MixLabBlobGateway> _logger;

        public MixLabBlobGateway(
            IOptions<MixLabStorageOptions> options,
            TokenCredential credential,
            ILogger<MixLabBlobGateway> logger)
        {
            var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            credential = credential ?? throw new ArgumentNullException(nameof(credential));

            _containerClient = MixLabBlobContainerClientFactory.Create(resolved, credential);
        }

        public async Task<MixLabBlobReadResult?> ReadAsync(string blobPath, CancellationToken cancellationToken)
        {
            try
            {
                var blobClient = _containerClient.GetBlobClient(blobPath);
                var download = await blobClient
                    .DownloadContentAsync(cancellationToken)
                    .ConfigureAwait(false);

                return new MixLabBlobReadResult(
                    download.Value.Content.ToArray(),
                    download.Value.Details.ETag.ToString());
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<string> WriteAsync(
            string blobPath,
            ReadOnlyMemory<byte> content,
            string? expectedETag,
            CancellationToken cancellationToken)
        {
            await _containerClient
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Optimistic concurrency: If-Match the read ETag, or create-only (If-None-Match: *)
            // when there was no prior blob. See BlobMixCatalogueRepository.WriteAsync for the same
            // pattern applied to the mix catalogue blob.
            var conditions = new BlobRequestConditions();
            if (expectedETag is null)
            {
                conditions.IfNoneMatch = ETag.All;
            }
            else
            {
                conditions.IfMatch = new ETag(expectedETag);
            }

            var blobClient = _containerClient.GetBlobClient(blobPath);

            try
            {
                using var stream = new MemoryStream(content.ToArray());
                var result = await blobClient
                    .UploadAsync(stream, new BlobUploadOptions { Conditions = conditions }, cancellationToken)
                    .ConfigureAwait(false);

                return result.Value.ETag.ToString();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                _logger.LogWarning(
                    ex,
                    "MixLab blob write conflict (status {Status}) for {BlobPath} — concurrent modification detected. expectedETag={ExpectedETag}",
                    ex.Status,
                    blobPath,
                    expectedETag ?? "(create-only)");
                throw new MixLabConcurrencyException(
                    $"MixLab blob write to '{blobPath}' was rejected because the blob changed concurrently.", ex);
            }
        }

        public async Task<Stream> OpenReadStreamAsync(string blobPath, CancellationToken cancellationToken)
        {
            var blobClient = _containerClient.GetBlobClient(blobPath);
            return await blobClient
                .OpenReadAsync(options: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task WriteStreamAsync(string blobPath, Stream content, CancellationToken cancellationToken)
        {
            await _containerClient
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var blobClient = _containerClient.GetBlobClient(blobPath);
            var conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };

            try
            {
                await blobClient
                    .UploadAsync(content, new BlobUploadOptions { Conditions = conditions }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                throw new MixLabConcurrencyException(
                    $"MixLab artifact blob '{blobPath}' already exists and is immutable.", ex);
            }
        }

        public async Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken)
        {
            var blobClient = _containerClient.GetBlobClient(blobPath);
            Response<bool> response = await blobClient.ExistsAsync(cancellationToken).ConfigureAwait(false);
            return response.Value;
        }

        public async Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
        {
            var blobClient = _containerClient.GetBlobClient(blobPath);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
