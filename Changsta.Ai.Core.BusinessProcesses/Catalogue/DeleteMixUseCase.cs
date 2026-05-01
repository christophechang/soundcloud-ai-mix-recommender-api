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
