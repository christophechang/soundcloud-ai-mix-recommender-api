using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Interface.Api.Controllers;
using Changsta.Ai.Interface.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class MixLabHistoryControllerTests
    {
        [Test]
        public async Task GetHistoryAsync_missing_history_returns_404()
        {
            MixLabHistoryController sut = BuildSut(getResult: null);

            IActionResult result = await sut.GetHistoryAsync(CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task GetHistoryAsync_existing_history_returns_content_and_etag_header()
        {
            var snapshot = new MixLabHistorySnapshot("{\"concepts\":[]}", "etag-1");
            MixLabHistoryController sut = BuildSut(getResult: snapshot);

            IActionResult result = await sut.GetHistoryAsync(CancellationToken.None);

            var content = result.Should().BeOfType<ContentResult>().Subject;
            content.Content.Should().Be("{\"concepts\":[]}");
            content.ContentType.Should().Be("application/json");
            sut.Response.Headers["ETag"].ToString().Should().Be("etag-1");
        }

        [Test]
        public async Task PutHistoryAsync_written_sets_etag_header_and_passes_if_match_through()
        {
            var spy = new SpyPutMixLabHistoryUseCase(new PutMixLabHistoryResult
            {
                Outcome = PutMixLabHistoryResult.PutOutcome.Written,
                ETag = "etag-2",
            });
            MixLabHistoryController sut = BuildSut(putUseCase: spy);
            SetRequestBody(sut, "{\"concepts\":[]}", ifMatch: "etag-1");

            IActionResult result = await sut.PutHistoryAsync(CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
            sut.Response.Headers["ETag"].ToString().Should().Be("etag-2");
            spy.ContentReceived.Should().Be("{\"concepts\":[]}");
            spy.IfMatchReceived.Should().Be("etag-1");
        }

        [Test]
        public async Task PutHistoryAsync_invalid_json_returns_400()
        {
            var stub = new StubPutMixLabHistoryUseCase(new PutMixLabHistoryResult
            {
                Outcome = PutMixLabHistoryResult.PutOutcome.InvalidJson,
                ErrorMessage = "bad json",
            });
            MixLabHistoryController sut = BuildSut(putUseCase: stub);
            SetRequestBody(sut, "{not json", ifMatch: null);

            IActionResult result = await sut.PutHistoryAsync(CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task PutHistoryAsync_precondition_failed_returns_412()
        {
            var stub = new StubPutMixLabHistoryUseCase(new PutMixLabHistoryResult
            {
                Outcome = PutMixLabHistoryResult.PutOutcome.PreconditionFailed,
                ErrorMessage = "stale",
            });
            MixLabHistoryController sut = BuildSut(putUseCase: stub);
            SetRequestBody(sut, "{\"concepts\":[]}", ifMatch: "stale-etag");

            IActionResult result = await sut.PutHistoryAsync(CancellationToken.None);

            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(412);
        }

        [Test]
        public void Route_is_api_mixlab()
        {
            var routeAttribute = typeof(MixLabHistoryController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>()
                .Single();

            routeAttribute.Template.Should().Be("api/mixlab");
        }

        [Test]
        public void Controller_requires_BearerSecret_for_MixLab_ApiSecret()
        {
            var bearerSecretAttribute = typeof(MixLabHistoryController)
                .GetCustomAttributes(typeof(BearerSecretAttribute), inherit: false)
                .Cast<BearerSecretAttribute>()
                .Single();

            GetConfigurationKey(bearerSecretAttribute).Should().Be("MixLab:ApiSecret");
        }

        [Test]
        public void PutHistoryAsync_enforces_8_megabyte_request_size_limit()
        {
            var requestSizeLimitAttribute = typeof(MixLabHistoryController)
                .GetMethod(nameof(MixLabHistoryController.PutHistoryAsync)) !
                .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false)
                .Cast<RequestSizeLimitAttribute>()
                .Single();

            GetRequestSizeLimitBytes(requestSizeLimitAttribute).Should().Be(8 * 1024 * 1024);
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

        private static void SetRequestBody(MixLabHistoryController sut, string body, string? ifMatch)
        {
            sut.ControllerContext.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            if (ifMatch is not null)
            {
                sut.ControllerContext.HttpContext.Request.Headers["If-Match"] = ifMatch;
            }
        }

        private static MixLabHistoryController BuildSut(
            MixLabHistorySnapshot? getResult = null,
            IPutMixLabHistoryUseCase? putUseCase = null)
        {
            var sut = new MixLabHistoryController(
                new StubGetMixLabHistoryUseCase(getResult),
                putUseCase ?? new StubPutMixLabHistoryUseCase(new PutMixLabHistoryResult
                {
                    Outcome = PutMixLabHistoryResult.PutOutcome.Written,
                    ETag = "etag-x",
                }));

            sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return sut;
        }

        private sealed class StubGetMixLabHistoryUseCase : IGetMixLabHistoryUseCase
        {
            private readonly MixLabHistorySnapshot? _snapshot;

            public StubGetMixLabHistoryUseCase(MixLabHistorySnapshot? snapshot)
            {
                _snapshot = snapshot;
            }

            public Task<MixLabHistorySnapshot?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_snapshot);
        }

        private sealed class StubPutMixLabHistoryUseCase : IPutMixLabHistoryUseCase
        {
            private readonly PutMixLabHistoryResult _result;

            public StubPutMixLabHistoryUseCase(PutMixLabHistoryResult result)
            {
                _result = result;
            }

            public Task<PutMixLabHistoryResult> PutAsync(string content, string? ifMatchETag, CancellationToken cancellationToken) =>
                Task.FromResult(_result);
        }

        private sealed class SpyPutMixLabHistoryUseCase : IPutMixLabHistoryUseCase
        {
            private readonly PutMixLabHistoryResult _result;

            public SpyPutMixLabHistoryUseCase(PutMixLabHistoryResult result)
            {
                _result = result;
            }

            public string? ContentReceived { get; private set; }

            public string? IfMatchReceived { get; private set; }

            public Task<PutMixLabHistoryResult> PutAsync(string content, string? ifMatchETag, CancellationToken cancellationToken)
            {
                ContentReceived = content;
                IfMatchReceived = ifMatchETag;
                return Task.FromResult(_result);
            }
        }
    }
}
