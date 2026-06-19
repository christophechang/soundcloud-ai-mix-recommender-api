using Changsta.Ai.Core.Contracts.Catalogue;
using Changsta.Ai.Core.Contracts.Radio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    public static class RadioServiceCollectionExtensions
    {
        // Registers the radio scheduling stack. The scheduler and use case live behind
        // internal types, so the wiring is done here (inside this assembly) rather than in
        // the API composition root. See issue #51.
        public static IServiceCollection AddRadioScheduling(this IServiceCollection services)
        {
            services.AddScoped<IRadioScheduler, RadioScheduler>();
            services.AddScoped<IGetRadioScheduleUseCase>(sp => new GetRadioScheduleUseCase(
                sp.GetRequiredService<IMixCatalogueProvider>(),
                sp.GetRequiredService<ILogger<GetRadioScheduleUseCase>>(),
                sp.GetRequiredService<IRadioScheduler>()));
            return services;
        }
    }
}
