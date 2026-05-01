using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Core.Normalization;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    internal sealed class BlobCatalogMixDeleter : ICatalogMixDeleter
    {
        private readonly IBlobMixCatalogueRepository _repository;
        private readonly ILogger<BlobCatalogMixDeleter> _logger;

        public BlobCatalogMixDeleter(IBlobMixCatalogueRepository repository, ILogger<BlobCatalogMixDeleter> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> DeleteBySlugAsync(string slug, CancellationToken cancellationToken)
        {
            IReadOnlyList<Mix> mixes = await _repository.ReadAsync(cancellationToken).ConfigureAwait(false);

            Mix? target = mixes.FirstOrDefault(m =>
                string.Equals(MixSlugHelper.ExtractSlug(m.Url), slug, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                return false;
            }

            Mix[] remaining = mixes
                .Where(m => !string.Equals(MixSlugHelper.ExtractSlug(m.Url), slug, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await _repository.WriteAsync(remaining, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Deleted mix {Slug} ({Title}) from blob catalog.", slug, target.Title);

            return true;
        }
    }
}
