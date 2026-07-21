using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Domain;
using Changsta.Ai.Infrastructure.Services.Azure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Infrastructure.Services.Azure.Catalogue
{
    /// <summary>
    /// Decides whether a rebuilt catalogue is worth writing back, and writes it. A write is never
    /// attempted when the blob read failed, since the merged result would be RSS-only and would
    /// overwrite intact durable data.
    /// </summary>
    internal sealed class CataloguePersister : ICataloguePersister
    {
        private readonly IBlobMixCatalogueRepository _repository;
        private readonly ILogger _logger;

        public CataloguePersister(IBlobMixCatalogueRepository repository, ILogger logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PersistIfChangedAsync(
            IReadOnlyList<Mix> merged,
            CatalogueLoadResult load,
            bool derivedFieldsChanged,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(load);

            int newDiscoveries = MixCatalogueMerger.CountNewDiscoveries(load.BlobMixes, load.RssMixes);
            int updatedEntries = MixCatalogueMerger.CountUpdatedEntries(load.BlobMixes, load.RssMixes);

            bool hasChanges =
                newDiscoveries > 0 || updatedEntries > 0 || load.BlobGenresChanged || derivedFieldsChanged;

            if (!load.BlobReadSucceeded || !hasChanges)
            {
                return;
            }

            _logger.LogInformation(
                "Writing blob catalog — {NewCount} new mixes, {UpdateCount} updated entries, genreNormalizationChanged={GenreNormalizationChanged}.",
                newDiscoveries,
                updatedEntries,
                load.BlobGenresChanged);

            try
            {
                await _repository.WriteAsync(merged, load.BlobETag, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Includes CatalogConcurrencyException: a concurrent writer changed the blob
                // between our read and write. Serve the in-memory merged result and let the
                // next refresh re-read and retry persistence.
                CatalogueMetrics.BlobWriteFailures.Add(1);
                _logger.LogWarning(ex, "Blob catalog write failed — serving the refreshed in-memory catalog and retrying persistence on the next load.");
            }
        }
    }
}
