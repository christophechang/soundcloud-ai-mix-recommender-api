using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;

namespace Changsta.Ai.Core.BusinessProcesses.Catalogue
{
    public sealed class DeleteMixUseCase : IDeleteMixUseCase
    {
        private const int CatalogMaxItems = 200;

        private readonly ICatalogMixDeleter _deleter;
        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly IMixCatalogueProvider _catalogueProvider;

        public DeleteMixUseCase(
            ICatalogMixDeleter deleter,
            ICatalogCacheInvalidator invalidator,
            IMixCatalogueProvider catalogueProvider)
        {
            _deleter = deleter ?? throw new ArgumentNullException(nameof(deleter));
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
        {
            // The deleter removes the mix from the durable blob catalogue under optimistic
            // concurrency (issue #34). We then invalidate the in-memory cache so the next read
            // reflects the deletion.
            //
            // RSS-window semantics (issue #89): the catalogue is merged from the blob plus the live
            // SoundCloud RSS feed. Deletion removes the mix from the blob, but if it is still present
            // in the RSS window it will be re-discovered on a refresh — this is expected, since RSS is
            // the source of truth for a mix's existence. Deletion is durable for mixes that have been
            // removed upstream (or aged out of the RSS window). Tombstones are deliberately not added
            // at this stage.
            bool deleted = await _deleter.DeleteBySlugAsync(id, cancellationToken).ConfigureAwait(false);

            if (deleted)
            {
                _invalidator.Invalidate();
                await _catalogueProvider.GetLatestAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }
    }
}
