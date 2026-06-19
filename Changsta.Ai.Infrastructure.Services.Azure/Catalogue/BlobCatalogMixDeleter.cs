using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Core.Normalization;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobCatalogMixDeleter : ICatalogMixDeleter
    {
        private const int MaxWriteAttempts = 3;

        private readonly IBlobMixCatalogueRepository _repository;
        private readonly ILogger<BlobCatalogMixDeleter> _logger;

        public BlobCatalogMixDeleter(IBlobMixCatalogueRepository repository, ILogger<BlobCatalogMixDeleter> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> DeleteBySlugAsync(string slug, CancellationToken cancellationToken)
        {
            // Read-modify-write under optimistic concurrency: if another writer (e.g. a refresh
            // write-back) changes the blob between our read and conditional write, re-read and
            // retry a bounded number of times so the delete is not silently lost. See issue #34.
            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
            {
                CatalogReadResult read = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);

                Mix? target = read.Mixes.FirstOrDefault(m =>
                    string.Equals(MixSlugHelper.ExtractSlug(m.Url), slug, StringComparison.OrdinalIgnoreCase));

                if (target is null)
                {
                    return false;
                }

                Mix[] remaining = read.Mixes
                    .Where(m => !string.Equals(MixSlugHelper.ExtractSlug(m.Url), slug, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                try
                {
                    await _repository.WriteAsync(remaining, read.ETag, cancellationToken).ConfigureAwait(false);
                }
                catch (CatalogConcurrencyException ex) when (attempt < MaxWriteAttempts)
                {
                    _logger.LogWarning(
                        ex,
                        "Delete of {Slug} hit a write conflict; re-reading and retrying ({Attempt}/{MaxAttempts}).",
                        slug,
                        attempt,
                        MaxWriteAttempts);
                    continue;
                }

                _logger.LogInformation("Deleted mix {Slug} ({Title}) from blob catalog.", slug, target.Title);
                return true;
            }

            _logger.LogError(
                "Delete of {Slug} failed after {MaxAttempts} attempts due to concurrent write conflicts.",
                slug,
                MaxWriteAttempts);
            throw new CatalogConcurrencyException(
                $"Could not delete mix '{slug}' after {MaxWriteAttempts} attempts because of concurrent catalogue writes.");
        }
    }
}
