using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class MixLabMapsControllerTests
    {
        [Test]
        public async Task RequestMapAsync_accepted_returns_202_with_job()
        {
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Queued);
            var sut = BuildSut(requestUseCase: new StubRequestUseCase(new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.Accepted,
                Job = job,
            }));

            IActionResult result = await sut.RequestMapAsync(Json("{\"uploadId\":\"u_1\"}"), CancellationToken.None);

            var jsonResult = result.Should().BeOfType<JsonResult>().Subject;
            jsonResult.StatusCode.Should().Be(202);
            jsonResult.Value.Should().BeSameAs(job);
        }

        [Test]
        public async Task RequestMapAsync_accepted_response_serializes_camelCase_with_lowercase_status()
        {
            // Guards the ManifestJsonOptions wiring itself: the Python worker parses this response,
            // so a camelCase property name and a lower-case enum string are binding contracts, not
            // incidental formatting. Serializing with the SerializerSettings actually attached to
            // the JsonResult means this fails if that wiring is ever dropped.
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Queued);
            var sut = BuildSut(requestUseCase: new StubRequestUseCase(new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.Accepted,
                Job = job,
            }));

            IActionResult result = await sut.RequestMapAsync(Json("{\"uploadId\":\"u_1\"}"), CancellationToken.None);

            string json = SerializeJsonResult(result);
            json.Should().Contain("\"uploadId\":\"u_1\"");
            json.Should().Contain("\"status\":\"queued\"");
        }

        [Test]
        public async Task RequestMapAsync_missing_uploadId_returns_400_without_calling_use_case()
        {
            var spy = new SpyRequestUseCase();
            var sut = BuildSut(requestUseCase: spy);

            IActionResult result = await sut.RequestMapAsync(Json("{}"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task RequestMapAsync_non_object_body_returns_400_without_calling_use_case()
        {
            var spy = new SpyRequestUseCase();
            var sut = BuildSut(requestUseCase: spy);

            IActionResult result = await sut.RequestMapAsync(Json("[]"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task RequestMapAsync_unknown_upload_returns_404()
        {
            var sut = BuildSut(requestUseCase: new StubRequestUseCase(new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.UnknownUpload,
            }));

            IActionResult result = await sut.RequestMapAsync(Json("{\"uploadId\":\"u_missing\"}"), CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task RequestMapAsync_no_uploads_available_returns_404()
        {
            var sut = BuildSut(requestUseCase: new StubRequestUseCase(new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.NoUploadsAvailable,
            }));

            IActionResult result = await sut.RequestMapAsync(Json("{\"uploadId\":\"latest\"}"), CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task RequestMapAsync_unknown_upload_and_no_uploads_available_produce_distinct_messages()
        {
            var unknownSut = BuildSut(requestUseCase: new StubRequestUseCase(new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.UnknownUpload,
            }));
            var noneSut = BuildSut(requestUseCase: new StubRequestUseCase(new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.NoUploadsAvailable,
            }));

            var unknownResult = (NotFoundObjectResult)await unknownSut.RequestMapAsync(Json("{\"uploadId\":\"u_missing\"}"), CancellationToken.None);
            var noneResult = (NotFoundObjectResult)await noneSut.RequestMapAsync(Json("{\"uploadId\":\"latest\"}"), CancellationToken.None);

            string unknownDetail = ExtractProblemDetail(unknownResult);
            string noneDetail = ExtractProblemDetail(noneResult);

            unknownDetail.Should().NotBe(noneDetail);
        }

        [Test]
        public async Task ClaimAsync_missing_workerId_returns_400_without_calling_use_case()
        {
            var spy = new SpyClaimUseCase();
            var sut = BuildSut(claimUseCase: spy);

            IActionResult result = await sut.ClaimAsync(Json("{}"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task ClaimAsync_blank_workerId_returns_400_without_calling_use_case()
        {
            var spy = new SpyClaimUseCase();
            var sut = BuildSut(claimUseCase: spy);

            IActionResult result = await sut.ClaimAsync(Json("{\"workerId\":\"   \"}"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task ClaimAsync_returns_200_with_job_when_claimed()
        {
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Running);
            var sut = BuildSut(claimUseCase: new StubClaimUseCase(job));

            IActionResult result = await sut.ClaimAsync(Json("{\"workerId\":\"worker-1\"}"), CancellationToken.None);

            var jsonResult = result.Should().BeOfType<JsonResult>().Subject;
            jsonResult.Value.Should().BeSameAs(job);
        }

        [Test]
        public async Task ClaimAsync_returns_204_when_nothing_claimable()
        {
            var sut = BuildSut(claimUseCase: new StubClaimUseCase(null));

            IActionResult result = await sut.ClaimAsync(Json("{\"workerId\":\"worker-1\"}"), CancellationToken.None);

            result.Should().BeOfType<NoContentResult>();
        }

        [Test]
        public async Task ClaimAsync_passes_workerId_through_to_use_case()
        {
            var spy = new SpyClaimUseCase();
            var sut = BuildSut(claimUseCase: spy);

            await sut.ClaimAsync(Json("{\"workerId\":\"worker-42\"}"), CancellationToken.None);

            spy.WorkerIdReceived.Should().Be("worker-42");
        }

        [Test]
        public async Task ClaimAsync_returns_200_response_serializes_camelCase_with_lowercase_status()
        {
            // See RequestMapAsync_accepted_response_serializes_camelCase_with_lowercase_status: same
            // binding-contract guard, for the claim response.
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Running);
            var sut = BuildSut(claimUseCase: new StubClaimUseCase(job));

            IActionResult result = await sut.ClaimAsync(Json("{\"workerId\":\"worker-1\"}"), CancellationToken.None);

            string json = SerializeJsonResult(result);
            json.Should().Contain("\"uploadId\":\"u_1\"");
            json.Should().Contain("\"status\":\"running\"");
        }

        [Test]
        public async Task ResultAsync_reads_raw_body_bytes_verbatim_and_passes_to_use_case()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"nested\":{\"a\":1,\"b\":[1,2,3]},\"unicode\":\"caf\\u00e9\"}");
            var spy = new SpyCompleteUseCase(returns: true);
            var sut = BuildSut(completeUseCase: spy);

            await InvokeResultAsync(sut, "u_1", payload);

            spy.PayloadReceived.ToArray().Should().Equal(payload);
            spy.UploadIdReceived.Should().Be("u_1");
        }

        [Test]
        public async Task ResultAsync_valid_json_returns_204()
        {
            var sut = BuildSut(completeUseCase: new SpyCompleteUseCase(returns: true));

            IActionResult result = await InvokeResultAsync(sut, "u_1", Encoding.UTF8.GetBytes("{\"ok\":true}"));

            result.Should().BeOfType<NoContentResult>();
        }

        [Test]
        public async Task ResultAsync_invalid_json_returns_400_without_calling_use_case()
        {
            var spy = new SpyCompleteUseCase(returns: true);
            var sut = BuildSut(completeUseCase: spy);

            IActionResult result = await InvokeResultAsync(sut, "u_1", Encoding.UTF8.GetBytes("not json"));

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task ResultAsync_use_case_refuses_returns_404()
        {
            var sut = BuildSut(completeUseCase: new SpyCompleteUseCase(returns: false));

            IActionResult result = await InvokeResultAsync(sut, "u_1", Encoding.UTF8.GetBytes("{\"ok\":true}"));

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public void ResultAsync_enforces_32_megabyte_request_size_limit()
        {
            var requestSizeLimitAttribute = typeof(MixLabMapsController)
                .GetMethod(nameof(MixLabMapsController.ResultAsync)) !
                .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false)
                .Cast<RequestSizeLimitAttribute>()
                .Single();

            GetRequestSizeLimitBytes(requestSizeLimitAttribute).Should().Be(32 * 1024 * 1024);
        }

        [Test]
        public async Task FailAsync_missing_error_returns_400_without_calling_use_case()
        {
            var spy = new SpyFailUseCase(returns: true);
            var sut = BuildSut(failUseCase: spy);

            IActionResult result = await sut.FailAsync("u_1", Json("{}"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task FailAsync_returns_204_when_failed()
        {
            var sut = BuildSut(failUseCase: new SpyFailUseCase(returns: true));

            IActionResult result = await sut.FailAsync("u_1", Json("{\"error\":\"boom\"}"), CancellationToken.None);

            result.Should().BeOfType<NoContentResult>();
        }

        [Test]
        public async Task FailAsync_returns_404_when_use_case_refuses()
        {
            var sut = BuildSut(failUseCase: new SpyFailUseCase(returns: false));

            IActionResult result = await sut.FailAsync("u_1", Json("{\"error\":\"boom\"}"), CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task FailAsync_passes_uploadId_and_error_through_to_use_case()
        {
            var spy = new SpyFailUseCase(returns: true);
            var sut = BuildSut(failUseCase: spy);

            await sut.FailAsync("u_1", Json("{\"error\":\"engine crashed\"}"), CancellationToken.None);

            spy.UploadIdReceived.Should().Be("u_1");
            spy.ErrorReceived.Should().Be("engine crashed");
        }

        [Test]
        public async Task GetMapAsync_returns_404_when_not_found()
        {
            var sut = BuildSut(getUseCase: new StubGetUseCase(new GetMixLabMapResult
            {
                Outcome = GetMixLabMapResult.GetOutcome.NotFound,
            }));

            IActionResult result = await sut.GetMapAsync("u_missing", CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task GetMapAsync_queued_job_returns_200_with_null_payload()
        {
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Queued);
            var sut = BuildSut(getUseCase: new StubGetUseCase(new GetMixLabMapResult
            {
                Outcome = GetMixLabMapResult.GetOutcome.Found,
                Job = job,
                Payload = null,
            }));

            IActionResult result = await sut.GetMapAsync("u_1", CancellationToken.None);

            var jsonResult = result.Should().BeOfType<JsonResult>().Subject;
            GetAnonymousProperty(jsonResult.Value!, "job").Should().BeSameAs(job);
            GetAnonymousProperty(jsonResult.Value!, "payload").Should().BeNull();
        }

        [Test]
        public async Task GetMapAsync_response_serializes_camelCase_with_lowercase_status()
        {
            // See RequestMapAsync_accepted_response_serializes_camelCase_with_lowercase_status: same
            // binding-contract guard, for the get response. The property-name assertion alone would
            // not catch SerializerSettings being dropped here, since the app's global MVC options
            // already camelCase property names — it's specifically the enum-as-lowercase-string that
            // depends on ManifestJsonOptions's JsonStringEnumConverter.
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Queued);
            var sut = BuildSut(getUseCase: new StubGetUseCase(new GetMixLabMapResult
            {
                Outcome = GetMixLabMapResult.GetOutcome.Found,
                Job = job,
                Payload = null,
            }));

            IActionResult result = await sut.GetMapAsync("u_1", CancellationToken.None);

            string json = SerializeJsonResult(result);
            json.Should().Contain("\"uploadId\":\"u_1\"");
            json.Should().Contain("\"status\":\"queued\"");
        }

        [Test]
        public async Task GetMapAsync_succeeded_job_with_missing_blob_returns_200_with_null_payload_and_does_not_crash()
        {
            // Edge case called out explicitly in the task brief: a succeeded job whose payload blob
            // is missing must still serve 200 {job, payload: null}, never throw.
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Succeeded);
            var sut = BuildSut(getUseCase: new StubGetUseCase(new GetMixLabMapResult
            {
                Outcome = GetMixLabMapResult.GetOutcome.Found,
                Job = job,
                Payload = null,
            }));

            IActionResult result = await sut.GetMapAsync("u_1", CancellationToken.None);

            var jsonResult = result.Should().BeOfType<JsonResult>().Subject;
            GetAnonymousProperty(jsonResult.Value!, "payload").Should().BeNull();
        }

        [Test]
        public async Task GetMapAsync_succeeded_job_embeds_payload_unescaped_and_round_trips_byte_meaning_identical()
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"directions\":[{\"key\":\"8A\",\"count\":3}],\"nested\":{\"x\":[1,2,3]}}");
            MixLabMapJob job = BuildJob("u_1", MixLabMapStatus.Succeeded);
            var sut = BuildSut(getUseCase: new StubGetUseCase(new GetMixLabMapResult
            {
                Outcome = GetMixLabMapResult.GetOutcome.Found,
                Job = job,
                Payload = payload,
            }));

            IActionResult result = await sut.GetMapAsync("u_1", CancellationToken.None);

            var jsonResult = result.Should().BeOfType<JsonResult>().Subject;
            object? payloadValue = GetAnonymousProperty(jsonResult.Value!, "payload");
            payloadValue.Should().NotBeNull();
            var embedded = (JsonElement)payloadValue!;

            // Embedded as nested JSON, not a JSON-encoded string.
            embedded.ValueKind.Should().Be(JsonValueKind.Object);

            using JsonDocument original = JsonDocument.Parse(payload);
            JsonElement.DeepEquals(embedded, original.RootElement).Should().BeTrue();
        }

        [Test]
        public void Route_is_api_mixlab()
        {
            var routeAttribute = typeof(MixLabMapsController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>()
                .Single();

            routeAttribute.Template.Should().Be("api/mixlab");
        }

        [Test]
        public void Controller_requires_BearerSecret_for_MixLab_ApiSecret()
        {
            var bearerSecretAttribute = typeof(MixLabMapsController)
                .GetCustomAttributes(typeof(BearerSecretAttribute), inherit: false)
                .Cast<BearerSecretAttribute>()
                .Single();

            GetConfigurationKey(bearerSecretAttribute).Should().Be("MixLab:ApiSecret");
        }

        private static long GetRequestSizeLimitBytes(RequestSizeLimitAttribute attribute)
        {
            return (long)typeof(RequestSizeLimitAttribute)
                .GetField("_bytes", BindingFlags.NonPublic | BindingFlags.Instance) !
                .GetValue(attribute) !;
        }

        private static string GetConfigurationKey(BearerSecretAttribute attribute)
        {
            return (string)typeof(BearerSecretAttribute)
                .GetField("_configurationKey", BindingFlags.NonPublic | BindingFlags.Instance) !
                .GetValue(attribute) !;
        }

        private static string ExtractProblemDetail(NotFoundObjectResult result)
        {
            object value = result.Value!;
            PropertyInfo? property = value.GetType().GetProperty("Detail");
            return (string)property !.GetValue(value) !;
        }

        private static object? GetAnonymousProperty(object value, string propertyName)
        {
            return value.GetType().GetProperty(propertyName) !.GetValue(value);
        }

        /// <summary>
        /// Serializes a <see cref="JsonResult"/>'s value with the <see cref="JsonSerializerOptions"/>
        /// actually attached to it via <see cref="JsonResult.SerializerSettings"/>, rather than a
        /// fresh default-constructed one — so a test using this fails if the controller ever stops
        /// wiring ManifestJsonOptions onto the result.
        /// </summary>
        private static string SerializeJsonResult(IActionResult result)
        {
            var jsonResult = (JsonResult)result;
            var options = (JsonSerializerOptions)jsonResult.SerializerSettings !;
            return JsonSerializer.Serialize(jsonResult.Value, options);
        }

        private static MixLabMapJob BuildJob(string uploadId, MixLabMapStatus status) => new()
        {
            UploadId = uploadId,
            Status = status,
            RequestedAt = DateTimeOffset.UtcNow,
        };

        private static JsonElement Json(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static async Task<IActionResult> InvokeResultAsync(MixLabMapsController sut, string uploadId, byte[] body)
        {
            var httpContext = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(body) },
            };
            sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

            return await sut.ResultAsync(uploadId, CancellationToken.None);
        }

        private static MixLabMapsController BuildSut(
            IRequestMixLabMapUseCase? requestUseCase = null,
            IClaimMixLabMapUseCase? claimUseCase = null,
            ICompleteMixLabMapUseCase? completeUseCase = null,
            IFailMixLabMapUseCase? failUseCase = null,
            IGetMixLabMapUseCase? getUseCase = null)
        {
            var sut = new MixLabMapsController(
                requestUseCase ?? new StubRequestUseCase(new RequestMixLabMapResult
                {
                    Outcome = RequestMixLabMapResult.RequestOutcome.Accepted,
                    Job = BuildJob("u_1", MixLabMapStatus.Queued),
                }),
                claimUseCase ?? new StubClaimUseCase(null),
                completeUseCase ?? new SpyCompleteUseCase(returns: true),
                failUseCase ?? new SpyFailUseCase(returns: true),
                getUseCase ?? new StubGetUseCase(new GetMixLabMapResult { Outcome = GetMixLabMapResult.GetOutcome.NotFound }));

            sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return sut;
        }

        private sealed class StubRequestUseCase : IRequestMixLabMapUseCase
        {
            private readonly RequestMixLabMapResult _result;

            public StubRequestUseCase(RequestMixLabMapResult result)
            {
                _result = result;
            }

            public Task<RequestMixLabMapResult> RequestAsync(string uploadId, CancellationToken cancellationToken) =>
                Task.FromResult(_result);
        }

        private sealed class SpyRequestUseCase : IRequestMixLabMapUseCase
        {
            public bool Called { get; private set; }

            public Task<RequestMixLabMapResult> RequestAsync(string uploadId, CancellationToken cancellationToken)
            {
                Called = true;
                return Task.FromResult(new RequestMixLabMapResult
                {
                    Outcome = RequestMixLabMapResult.RequestOutcome.Accepted,
                    Job = BuildJob(uploadId, MixLabMapStatus.Queued),
                });
            }
        }

        private sealed class StubClaimUseCase : IClaimMixLabMapUseCase
        {
            private readonly MixLabMapJob? _job;

            public StubClaimUseCase(MixLabMapJob? job)
            {
                _job = job;
            }

            public Task<MixLabMapJob?> ClaimAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult(_job);
        }

        private sealed class SpyClaimUseCase : IClaimMixLabMapUseCase
        {
            public bool Called { get; private set; }

            public string? WorkerIdReceived { get; private set; }

            public Task<MixLabMapJob?> ClaimAsync(string workerId, CancellationToken cancellationToken)
            {
                Called = true;
                WorkerIdReceived = workerId;
                return Task.FromResult<MixLabMapJob?>(null);
            }
        }

        private sealed class SpyCompleteUseCase : ICompleteMixLabMapUseCase
        {
            private readonly bool _returns;

            public SpyCompleteUseCase(bool returns)
            {
                _returns = returns;
            }

            public bool Called { get; private set; }

            public string? UploadIdReceived { get; private set; }

            public ReadOnlyMemory<byte> PayloadReceived { get; private set; }

            public Task<bool> CompleteAsync(string uploadId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            {
                Called = true;
                UploadIdReceived = uploadId;
                PayloadReceived = payload;
                return Task.FromResult(_returns);
            }
        }

        private sealed class SpyFailUseCase : IFailMixLabMapUseCase
        {
            private readonly bool _returns;

            public SpyFailUseCase(bool returns)
            {
                _returns = returns;
            }

            public bool Called { get; private set; }

            public string? UploadIdReceived { get; private set; }

            public string? ErrorReceived { get; private set; }

            public Task<bool> FailAsync(string uploadId, string error, CancellationToken cancellationToken)
            {
                Called = true;
                UploadIdReceived = uploadId;
                ErrorReceived = error;
                return Task.FromResult(_returns);
            }
        }

        private sealed class StubGetUseCase : IGetMixLabMapUseCase
        {
            private readonly GetMixLabMapResult _result;

            public StubGetUseCase(GetMixLabMapResult result)
            {
                _result = result;
            }

            public Task<GetMixLabMapResult> GetAsync(string uploadId, CancellationToken cancellationToken) =>
                Task.FromResult(_result);
        }
    }
}
