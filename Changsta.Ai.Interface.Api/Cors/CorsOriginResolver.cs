using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Changsta.Ai.Interface.Api.Cors
{
    /// <summary>
    /// Resolves the CORS allowed-origins list from configuration (<c>Cors:AllowedOrigins</c>),
    /// appending dev-only origins in Development and validating that production never allows
    /// non-https or localhost origins. See issue #32.
    /// </summary>
    public static class CorsOriginResolver
    {
        // Dev-only convenience origins appended on top of the configured list.
        private static readonly string[] DevelopmentOrigins = { "http://localhost:8080" };

        public static string[] Resolve(IConfiguration configuration, IHostEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);

            string[] configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();

            var origins = new List<string>();
            foreach (string origin in configured)
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    origins.Add(origin.Trim());
                }
            }

            if (environment.IsDevelopment())
            {
                origins.AddRange(DevelopmentOrigins);
            }
            else
            {
                ValidateProductionOrigins(origins);
            }

            return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void ValidateProductionOrigins(IReadOnlyList<string> origins)
        {
            if (origins.Count == 0)
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must contain at least one https origin in non-Development environments.");
            }

            foreach (string origin in origins)
            {
                if (!origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"CORS origin '{origin}' is rejected: only https origins are permitted outside Development.");
                }

                if (origin.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"CORS origin '{origin}' is rejected: localhost origins are not permitted outside Development.");
                }
            }
        }
    }
}
