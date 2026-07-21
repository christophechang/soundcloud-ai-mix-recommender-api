using System.Collections.Generic;
using System.Text.Json;
using Changsta.Ai.Interface.Api.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.Errors
{
    [TestFixture]
    public sealed class ApiProblemTests
    {
        [Test]
        public void Create_carries_detail_into_the_legacy_error_field()
        {
            ProblemDetails problem = ApiProblem.Create(StatusCodes.Status400BadRequest, "slug is required.");

            problem.Status.Should().Be(StatusCodes.Status400BadRequest);
            problem.Title.Should().Be("Bad Request");
            problem.Detail.Should().Be("slug is required.");
            problem.Extensions["error"].Should().Be("slug is required.");
        }

        [Test]
        public void Create_omits_correlationId_when_none_is_supplied()
        {
            ProblemDetails problem = ApiProblem.Create(StatusCodes.Status404NotFound, "missing");

            problem.Extensions.ContainsKey("correlationId").Should().BeFalse();
        }

        [Test]
        public void Create_falls_back_to_the_status_title_when_detail_is_null()
        {
            ProblemDetails problem = ApiProblem.Create(StatusCodes.Status500InternalServerError, detail: null);

            problem.Detail.Should().Be("Error");
            problem.Extensions["error"].Should().Be("Error");
        }

        [Test]
        public void Extensions_serialise_at_the_json_root()
        {
            ProblemDetails problem = ApiProblem.Create(
                StatusCodes.Status503ServiceUnavailable,
                "Station 'crucial-fm' has no eligible mixes in the catalogue.",
                correlationId: "trace-123",
                new Dictionary<string, object?> { ["stationId"] = "crucial-fm" });

            string json = JsonSerializer.Serialize(
                problem,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            root.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status503ServiceUnavailable);
            root.GetProperty("stationId").GetString().Should().Be("crucial-fm");
            root.GetProperty("correlationId").GetString().Should().Be("trace-123");
            root.GetProperty("error").GetString().Should().Contain("crucial-fm");
        }

        [Test]
        public void ValidationFailed_keeps_traceId_and_the_per_field_errors()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/api/mixes/recommend";

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor());

            actionContext.ModelState.AddModelError("Question", "The Question field is required.");

            BadRequestObjectResult result = ApiProblem.ValidationFailed(actionContext);
            var problem = (ValidationProblemDetails)result.Value!;

            // traceId is what the framework default emitted; dropping it would silently break
            // anything already correlating on it.
            problem.Extensions.Should().ContainKey("traceId");
            problem.Extensions["traceId"].Should().NotBeNull();

            problem.Errors.Should().ContainKey("Question");
            problem.Status.Should().Be(StatusCodes.Status400BadRequest);
            problem.Instance.Should().Be("/api/mixes/recommend");
            problem.Extensions["error"].Should().Be("The request failed validation.");
            problem.Extensions.Should().ContainKey("correlationId");
            result.ContentTypes.Should().Contain("application/problem+json");
        }

        [Test]
        public void Result_helpers_set_the_problem_json_content_type()
        {
            ApiProblem.BadRequest("bad").ContentTypes.Should().Contain("application/problem+json");
            ApiProblem.NotFound("missing").ContentTypes.Should().Contain("application/problem+json");
            ApiProblem.Unauthorized("nope").ContentTypes.Should().Contain("application/problem+json");
            ApiProblem.Status(StatusCodes.Status409Conflict, "conflict").ContentTypes
                .Should().Contain("application/problem+json");
        }

        [Test]
        public void Result_helpers_keep_the_status_code_on_the_result()
        {
            ApiProblem.BadRequest("bad").StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            ApiProblem.NotFound("missing").StatusCode.Should().Be(StatusCodes.Status404NotFound);
            ApiProblem.Unauthorized("nope").StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            ApiProblem.Status(StatusCodes.Status412PreconditionFailed, "stale").StatusCode
                .Should().Be(StatusCodes.Status412PreconditionFailed);
        }
    }
}
