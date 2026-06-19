using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Changsta.Ai.Interface.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Changsta.Ai.Tests.Unit.Middleware
{
    [TestFixture]
    public sealed class GlobalExceptionMiddlewareTests
    {
        [Test]
        public async Task TimeoutException_maps_to_503()
        {
            int status = await InvokeWithException(new TimeoutException("OpenAI request timed out after 30s."));

            status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }

        [Test]
        public async Task HttpRequestException_maps_to_503()
        {
            int status = await InvokeWithException(new HttpRequestException("connection refused"));

            status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }

        [Test]
        public async Task ArgumentException_maps_to_400()
        {
            int status = await InvokeWithException(new ArgumentException("bad input"));

            status.Should().Be(StatusCodes.Status400BadRequest);
        }

        [Test]
        public async Task UnexpectedException_maps_to_500()
        {
            int status = await InvokeWithException(new InvalidOperationException("boom"));

            status.Should().Be(StatusCodes.Status500InternalServerError);
        }

        private static async Task<int> InvokeWithException(Exception thrown)
        {
            RequestDelegate next = _ => throw thrown;
            var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            return context.Response.StatusCode;
        }
    }
}
