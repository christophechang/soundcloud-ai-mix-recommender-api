using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Changsta.Ai.Interface.Api.RateLimiting
{
    /// <summary>
    /// Central rate-limiting configuration. A global per-IP default policy protects every endpoint;
    /// stricter named policies apply to the expensive AI endpoint and the privileged mutation/
    /// diagnostics endpoints. See issue #35.
    /// </summary>
    public static class RateLimitPolicies
    {
        public const string Recommend = "recommend";
        public const string Privileged = "privileged";

        public const int DefaultPermitLimit = 60;
        public const int RecommendPermitLimit = 10;
        public const int PrivilegedPermitLimit = 5;
        public const int RetryAfterSeconds = 60;

        public static void Configure(RateLimiterOptions options, bool trustCloudflareHeader)
        {
            ArgumentNullException.ThrowIfNull(options);

            // Global default: every endpoint is throttled per client IP unless it opts out.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientPartitionKey(httpContext, trustCloudflareHeader),
                    _ => CreateWindowOptions(DefaultPermitLimit)));

            options.AddPolicy(Recommend, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientPartitionKey(httpContext, trustCloudflareHeader),
                    _ => CreateWindowOptions(RecommendPermitLimit)));

            options.AddPolicy(Privileged, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveClientPartitionKey(httpContext, trustCloudflareHeader),
                    _ => CreateWindowOptions(PrivilegedPermitLimit)));

            options.OnRejected = async (context, cancellationToken) =>
                await WriteRejectionAsync(context.HttpContext, cancellationToken).ConfigureAwait(false);
        }

        // CF-Connecting-IP is only trusted when RateLimiting:TrustCloudflareHeader is true (prod
        // behind Cloudflare). Otherwise the TCP RemoteIpAddress is used directly.
        public static string ResolveClientPartitionKey(HttpContext httpContext, bool trustCloudflareHeader)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            string? cloudflareIp = trustCloudflareHeader
                ? httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                : null;

            return (string.IsNullOrWhiteSpace(cloudflareIp) ? null : cloudflareIp)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";
        }

        public static FixedWindowRateLimiterOptions CreateWindowOptions(int permitLimit) =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            };

        public static async Task WriteRejectionAsync(HttpContext httpContext, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            httpContext.Response.ContentType = "application/json";
            httpContext.Response.Headers["Retry-After"] = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Please wait a moment and try again." },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
