using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Changsta.Ai.Interface.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Changsta.Ai.Interface.Api.Middleware
{
    public sealed class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string correlationId = context.TraceIdentifier;

                _logger.LogError(
                    ex,
                    "Unhandled exception. CorrelationId: {CorrelationId}",
                    correlationId);

                int statusCode = GetStatusCode(ex);

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var error = new ErrorResponse
                {
                    Message = GetSafeMessage(statusCode),
                    CorrelationId = correlationId,
                };

                string json = JsonSerializer.Serialize(error);

                await context.Response.WriteAsync(json).ConfigureAwait(false);
            }
        }

        private static int GetStatusCode(Exception ex)
        {
            if (ex is ArgumentException)
            {
                return StatusCodes.Status400BadRequest;
            }

            if (ex is HttpRequestException)
            {
                return StatusCodes.Status503ServiceUnavailable;
            }

            return StatusCodes.Status500InternalServerError;
        }

        private static string GetSafeMessage(int statusCode)
        {
            if (statusCode == StatusCodes.Status400BadRequest)
            {
                return "Invalid request.";
            }

            if (statusCode == StatusCodes.Status503ServiceUnavailable)
            {
                return "Service temporarily unavailable.";
            }

            return "An unexpected error occurred.";
        }
    }
}