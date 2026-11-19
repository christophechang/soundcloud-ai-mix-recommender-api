using System;
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

        // The background rewarm started by the most recent flush, for tests.
        internal Task? RewarmTask { get; private set; }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            _invalidator.Invalidate();

            // The rewarm can outlast the caller's patience (a cold catalogue on the free App Service
            // plan runs past Cloudflare's 100 second limit), and the request token is cancelled when
            // the caller gives up, which would abandon the warm half way. Once the cache is
            // invalidated the flush has done its job, so the rewarm runs detached from the request.
            RewarmTask = Task.Run(RewarmAsync, CancellationToken.None);
            return Task.CompletedTask;
        }

        private async Task RewarmAsync()
        {
            try
            {
                await _catalogueProvider.GetLatestAsync(CatalogMaxItems, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Nothing awaits this task; the next catalogue read retries the load.
                _logger.LogError(ex, "Catalogue rewarm after flush failed");
            }
        }
    }
}
