using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.Catalogue
{
    public sealed class CatalogFlushUseCase : ICatalogFlushUseCase
    {
        private const int CatalogMaxItems = 200;

        private readonly ICatalogCacheInvalidator _invalidator;
        private readonly IMixCatalogueProvider _catalogueProvider;
        private readonly ILogger<CatalogFlushUseCase> _logger;

        public CatalogFlushUseCase(
            ICatalogCacheInvalidator invalidator,
            IMixCatalogueProvider catalogueProvider,
            ILogger<CatalogFlushUseCase> logger)
        {
            _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
            _catalogueProvider = catalogueProvider ?? throw new ArgumentNullException(nameof(catalogueProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            _invalidator.Invalidate();

            try
            {
                await _catalogueProvider.GetLatestAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
            }
            catch (SecurityException)
            {
                _logger.LogWarning(
                    "Catalog cache was invalidated, but the immediate rewarm hit a runtime security validation condition. The next catalogue read will retry the cold load.");
            }
        }
    }
}
