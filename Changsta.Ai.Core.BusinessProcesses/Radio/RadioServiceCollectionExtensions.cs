using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Registers the radio scheduling stack against the supplied configuration. Stations and
        /// per-slot targets are product tuning, so they come from config/radio.json rather than a
        /// redeploy (see issue #43). Invalid configuration throws here — at startup — because the
        /// alternative is a station that silently schedules nothing.
        /// </summary>
        public static IServiceCollection AddRadioScheduling(
            this IServiceCollection services,
            RadioOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            IReadOnlyList<string> failures = RadioOptionsValidator.Validate(options);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "Radio configuration is invalid: " + string.Join(" ", failures));
            }

            var definitions = new RadioDefinitions(options);
            services.AddSingleton(definitions);

            services.AddScoped<IRadioScheduler>(sp => new RadioScheduler(
                sp.GetRequiredService<RadioDefinitions>()));
            services.AddScoped<IGetRadioScheduleUseCase>(sp => new GetRadioScheduleUseCase(
                sp.GetRequiredService<IMixCatalogueProvider>(),
                sp.GetRequiredService<ILogger<GetRadioScheduleUseCase>>(),
                sp.GetRequiredService<IRadioScheduler>(),
                sp.GetRequiredService<RadioDefinitions>()));
            return services;
        }
    }
}
