using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Changsta.Ai.Interface.Api.Security
{
    /// <summary>
    /// Authorises a request against a shared bearer secret read from configuration. Centralises the
    /// previously copy-pasted inline checks on the privileged endpoints (catalog flush/delete,
    /// diagnostics). Fails closed: outside Development a blank/unset secret rejects the request.
    /// Startup also refuses to boot in non-Development when the secret is unset, so the runtime
    /// non-Development branch here is defence-in-depth. See issue #31.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class BearerSecretAttribute : Attribute, IAuthorizationFilter
    {
        private const string BearerPrefix = "Bearer ";

        private readonly string _configurationKey;

        public BearerSecretAttribute(string configurationKey)
        {
            _configurationKey = configurationKey ?? throw new ArgumentNullException(nameof(configurationKey));
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            IServiceProvider services = context.HttpContext.RequestServices;
            var configuration = services.GetRequiredService<IConfiguration>();
            var environment = services.GetRequiredService<IHostEnvironment>();

            string? expectedSecret = configuration[_configurationKey];

            if (string.IsNullOrWhiteSpace(expectedSecret))
            {
                // Fail closed outside Development. In Development a missing secret skips the check
                // so local workflows aren't blocked.
                if (!environment.IsDevelopment())
                {
                    context.Result = Unauthorized();
                }

                return;
            }

            if (!TryGetBearerToken(context.HttpContext.Request, out string token)
                || !FixedTimeEquals(token, expectedSecret))
            {
                context.Result = Unauthorized();
            }
        }

        private static IActionResult Unauthorized() =>
            new UnauthorizedObjectResult(new { error = "Invalid or missing authorization." });

        private static bool TryGetBearerToken(HttpRequest request, out string token)
        {
            token = string.Empty;

            if (!request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                return false;
            }

            string value = authHeader.ToString();
            if (!value.StartsWith(BearerPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            token = value[BearerPrefix.Length..];
            return true;
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            byte[] left = Encoding.UTF8.GetBytes(a);
            byte[] right = Encoding.UTF8.GetBytes(b);

            // FixedTimeEquals returns false for differing lengths without short-circuiting the
            // per-byte comparison, avoiding the timing side-channel of string.Equals.
            return CryptographicOperations.FixedTimeEquals(left, right);
        }
    }
}
