using Changsta.Ai.Infrastructure.Services.Azure.Catalogue;
using Changsta.Ai.Infrastructure.Services.Azure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Changsta.Ai.Infrastructure.Services.Azure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAzureBlobMixCatalog(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<BlobCatalogOptions>(configuration.GetSection("Azure:BlobCatalog"));
            services.AddScoped<IBlobMixCatalogueRepository, BlobMixCatalogueRepository>();
            services.AddScoped<Changsta.Ai.Core.Contracts.Catalogue.ICatalogMixDeleter, BlobCatalogMixDeleter>();

            return services;
        }
    }
}
