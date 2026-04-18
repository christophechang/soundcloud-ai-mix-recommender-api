using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Catalogue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Interface.Api.Services
{
    internal sealed class CatalogWarmupService : IHostedService
    {
        private const int CatalogMaxItems = 200;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CatalogWarmupService> _logger;

        public CatalogWarmupService(IServiceScopeFactory scopeFactory, ILogger<CatalogWarmupService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var provider = scope.ServiceProvider.GetRequiredService<IMixCatalogueProvider>();
                await provider.GetLatestAsync(CatalogMaxItems, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Catalog cache warmed on startup.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog warmup failed — first request will be cold.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
