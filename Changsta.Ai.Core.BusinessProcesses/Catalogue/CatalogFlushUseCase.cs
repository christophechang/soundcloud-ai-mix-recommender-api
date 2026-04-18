using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;

namespace Changsta.Ai.Core.BusinessProcesses.Catalogue
{
    public sealed class CatalogFlushUseCase : ICatalogFlushUseCase
    {
        private const int CatalogMaxItems = 200;

        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly IMixCatalogueProvider _catalogueProvider;

        public CatalogFlushUseCase(ICatalogCacheInvalidator invalidator, IMixCatalogueProvider catalogueProvider)
        {
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            _invalidator.Invalidate();
            await _catalogueProvider.GetLatestAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
        }
    }
}
