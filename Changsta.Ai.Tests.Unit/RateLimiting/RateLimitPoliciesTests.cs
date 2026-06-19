using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Changsta.Ai.Interface.Api.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.RateLimiting
{
    [TestFixture]
    public sealed class RateLimitPoliciesTests
    {
        [Test]
        public void ResolveClientPartitionKey_uses_cloudflare_header_when_trusted()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

            string key = RateLimitPolicies.ResolveClientPartitionKey(httpContext, trustCloudflareHeader: true);

            key.Should().Be("203.0.113.7");
        }

        [Test]
        public void ResolveClientPartitionKey_ignores_cloudflare_header_when_not_trusted()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

            string key = RateLimitPolicies.ResolveClientPartitionKey(httpContext, trustCloudflareHeader: false);

            key.Should().Be("10.0.0.1");
        }

        [Test]
        public void ResolveClientPartitionKey_falls_back_to_unknown()
        {
            var httpContext = new DefaultHttpContext();

            string key = RateLimitPolicies.ResolveClientPartitionKey(httpContext, trustCloudflareHeader: true);

            key.Should().Be("unknown");
        }

        [Test]
        public async Task WriteRejectionAsync_sets_429_retry_after_and_body()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            await RateLimitPolicies.WriteRejectionAsync(httpContext, CancellationToken.None);

            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
            httpContext.Response.Headers["Retry-After"].ToString().Should().Be("60");

            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            string body = await new StreamReader(httpContext.Response.Body, Encoding.UTF8).ReadToEndAsync();
            body.Should().Contain("Too many requests");
        }

        [TestCase(RateLimitPolicies.RecommendPermitLimit)]
        [TestCase(RateLimitPolicies.PrivilegedPermitLimit)]
        [TestCase(RateLimitPolicies.DefaultPermitLimit)]
        public void Fixed_window_rejects_once_the_permit_limit_is_exhausted(int permitLimit)
        {
            using var limiter = new FixedWindowRateLimiter(RateLimitPolicies.CreateWindowOptions(permitLimit));

            for (int i = 0; i < permitLimit; i++)
            {
                limiter.AttemptAcquire(1).IsAcquired.Should().BeTrue();
            }

            limiter.AttemptAcquire(1).IsAcquired.Should().BeFalse();
        }
    }
}
