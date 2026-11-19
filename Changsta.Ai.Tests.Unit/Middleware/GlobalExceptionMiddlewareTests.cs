using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Changsta.Ai.Interface.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
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

        [Test]
        public async Task Failure_to_log_still_produces_the_error_response()
        {
            var logger = new ThrowingLogger();
            RequestDelegate next = _ => throw new InvalidOperationException("boom");
            var middleware = new GlobalExceptionMiddleware(next, logger);

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            logger.DetailFreeFallbackAttempted.Should().BeTrue();
        }

        [Test]
        public async Task Exception_after_the_response_started_is_rethrown()
        {
            RequestDelegate next = _ => throw new InvalidOperationException("boom");

            var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

            var context = new DefaultHttpContext();
            context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

            Func<Task> act = () => middleware.InvokeAsync(context);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        }

        [Test]
        public async Task Writes_problem_details_with_camelCase_and_the_legacy_fields()
        {
            RequestDelegate next = _ => throw new InvalidOperationException("boom");
            var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/catalog/mixes";
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            context.Response.ContentType.Should().Be("application/problem+json");

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            string body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
            root.GetProperty("title").GetString().Should().Be("Error");
            root.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
            root.GetProperty("instance").GetString().Should().Be("/api/catalog/mixes");

            // These two were PascalCase before the ProblemDetails move; clients read them at the root.
            root.GetProperty("error").GetString().Should().Be("An unexpected error occurred.");
            root.TryGetProperty("correlationId", out _).Should().BeTrue();
            root.TryGetProperty("Error", out _).Should().BeFalse();
        }

        [Test]
        public async Task Maps_service_unavailable_to_a_safe_message()
        {
            RequestDelegate next = _ => throw new HttpRequestException("upstream refused");
            var middleware = new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance);

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            string body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

            using JsonDocument document = JsonDocument.Parse(body);

            document.RootElement.GetProperty("error").GetString()
                .Should().Be("Service temporarily unavailable.");

            // The upstream exception message must not reach the caller.
            body.Should().NotContain("upstream refused");
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

        private sealed class StartedResponseFeature : IHttpResponseFeature
        {
            public int StatusCode { get; set; } = StatusCodes.Status200OK;

            public string? ReasonPhrase { get; set; }

            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

            public Stream Body { get; set; } = new MemoryStream();

            public bool HasStarted => true;

            public void OnStarting(Func<object, Task> callback, object state)
            {
            }

            public void OnCompleted(Func<object, Task> callback, object state)
            {
            }
        }

        // Reproduces the production failure mode where capturing exception detail throws.
        private sealed class ThrowingLogger : ILogger<GlobalExceptionMiddleware>
        {
            public bool DetailFreeFallbackAttempted { get; private set; }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullLogger.Instance.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is not null)
                {
                    throw new BadImageFormatException("Bad binary signature. (0x80131192)");
                }

                DetailFreeFallbackAttempted = true;
            }
        }
    }
}
