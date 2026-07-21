using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Interface.Api.Errors
{
    /// <summary>
    /// Builds the single error shape the API returns. Every non-2xx body is an RFC 7807
    /// <see cref="ProblemDetails"/>; the legacy <c>error</c> and <c>correlationId</c> fields are
    /// carried as extensions, which serialise at the JSON root, so existing consumers that read
    /// those two properties keep working unchanged.
    /// </summary>
    public static class ApiProblem
    {
        public static ProblemDetails Create(int statusCode, string? detail, string? correlationId = null)
        {
            // A null detail means a use case reported failure without a message; fall back to the
            // status title so the body is never missing its human-readable part.
            string resolved = string.IsNullOrWhiteSpace(detail) ? TitleFor(statusCode) : detail;

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = TitleFor(statusCode),
                Detail = resolved,
            };

            problem.Extensions["error"] = resolved;

            if (!string.IsNullOrEmpty(correlationId))
            {
                problem.Extensions["correlationId"] = correlationId;
            }

            return problem;
        }

        public static ProblemDetails Create(
            int statusCode,
            string? detail,
            string? correlationId,
            IReadOnlyDictionary<string, object?> extensions)
        {
            ProblemDetails problem = Create(statusCode, detail, correlationId);

            foreach (KeyValuePair<string, object?> extension in extensions)
            {
                problem.Extensions[extension.Key] = extension.Value;
            }

            return problem;
        }

        public static BadRequestObjectResult BadRequest(string? detail) =>
            AsProblem(new BadRequestObjectResult(Create(StatusCodes.Status400BadRequest, detail)));

        public static NotFoundObjectResult NotFound(string? detail) =>
            AsProblem(new NotFoundObjectResult(Create(StatusCodes.Status404NotFound, detail)));

        public static UnauthorizedObjectResult Unauthorized(string? detail) =>
            AsProblem(new UnauthorizedObjectResult(Create(StatusCodes.Status401Unauthorized, detail)));

        public static ObjectResult Status(int statusCode, string? detail) =>
            AsProblem(new ObjectResult(Create(statusCode, detail)) { StatusCode = statusCode });

        public static ObjectResult Status(
            int statusCode,
            string? detail,
            IReadOnlyDictionary<string, object?> extensions) =>
            AsProblem(new ObjectResult(Create(statusCode, detail, correlationId: null, extensions))
            {
                StatusCode = statusCode,
            });

        private static T AsProblem<T>(T result)
            where T : ObjectResult
        {
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        private static string TitleFor(int statusCode) => statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status429TooManyRequests => "Too Many Requests",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            _ => "Error",
        };
    }
}
