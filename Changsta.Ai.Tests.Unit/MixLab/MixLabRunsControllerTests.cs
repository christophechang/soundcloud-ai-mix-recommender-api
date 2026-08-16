using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public sealed class MixLabRunsControllerTests
    {
        [Test]
        public async Task GetRunAsync_serialises_verdict_with_its_snake_case_wire_spelling()
        {
            // The blob layer writes "played_modified" and the web only knows that spelling; a
            // generic camelCase string-enum converter renders "playedModified", which would make a
            // modified-play cut invisible in the Session page's RELEASED half.
            MixLabRun run = BuildRun("r_1", MixLabFeedbackVerdict.PlayedModified);
            var sut = BuildSut(query: new StubRunQueryUseCase(run, Array.Empty<MixLabRunIndexEntry>()));

            IActionResult result = await sut.GetRunAsync("r_1", CancellationToken.None);

            string json = SerializeJsonResult(result);
            json.Should().Contain("\"verdict\":\"played_modified\"");
            json.Should().NotContain("playedModified");
        }

        [Test]
        public async Task GetRunAsync_still_serialises_status_as_lower_case_camel_string()
        {
            // Guards the rest of ManifestJsonOptions: adding the verdict converter must not displace
            // the generic string-enum converter the Python worker depends on.
            MixLabRun run = BuildRun("r_1", MixLabFeedbackVerdict.Played);
            var sut = BuildSut(query: new StubRunQueryUseCase(run, Array.Empty<MixLabRunIndexEntry>()));

            IActionResult result = await sut.GetRunAsync("r_1", CancellationToken.None);

            string json = SerializeJsonResult(result);
            json.Should().Contain("\"status\":\"succeeded\"");
            json.Should().Contain("\"runId\":\"r_1\"");
        }

        [Test]
        public async Task GetRunAsync_unknown_run_returns_404()
        {
            var sut = BuildSut(query: new StubRunQueryUseCase(null, Array.Empty<MixLabRunIndexEntry>()));

            IActionResult result = await sut.GetRunAsync("r_missing", CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public void Route_is_api_mixlab()
        {
            var routeAttribute = typeof(MixLabRunsController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>()
                .Single();

            routeAttribute.Template.Should().Be("api/mixlab");
        }

        [Test]
        public void Controller_requires_BearerSecret_for_MixLab_ApiSecret()
        {
            var bearerSecretAttribute = typeof(MixLabRunsController)
                .GetCustomAttributes(typeof(BearerSecretAttribute), inherit: false)
                .Cast<BearerSecretAttribute>()
                .Single();

            GetConfigurationKey(bearerSecretAttribute).Should().Be("MixLab:ApiSecret");
        }

        [Test]
        public async Task ReindexRunsAsync_returns_200_with_the_run_count()
        {
            var spy = new SpyReindexUseCase(returns: 12);
            var sut = BuildSut(reindex: spy);

            IActionResult result = await sut.ReindexRunsAsync(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            JsonSerializer.Serialize(ok.Value).Should().Be("{\"runs\":12}");
            spy.Called.Should().BeTrue();
        }

        [Test]
        public async Task ReindexRunsAsync_empty_archive_returns_zero()
        {
            var sut = BuildSut(reindex: new SpyReindexUseCase(returns: 0));

            IActionResult result = await sut.ReindexRunsAsync(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            JsonSerializer.Serialize(ok.Value).Should().Be("{\"runs\":0}");
        }

        private static string GetConfigurationKey(BearerSecretAttribute attribute)
        {
            return (string)typeof(BearerSecretAttribute)
                .GetField("_configurationKey", BindingFlags.NonPublic | BindingFlags.Instance) !
                .GetValue(attribute) !;
        }

        /// <summary>
        /// Serializes a <see cref="JsonResult"/>'s value with the options actually attached to it,
        /// so a test fails if the controller ever stops wiring ManifestJsonOptions onto the result.
        /// </summary>
        private static string SerializeJsonResult(IActionResult result)
        {
            var jsonResult = (JsonResult)result;
            var options = (JsonSerializerOptions)jsonResult.SerializerSettings !;
            return JsonSerializer.Serialize(jsonResult.Value, options);
        }

        private static MixLabRun BuildRun(string runId, MixLabFeedbackVerdict verdict) => new()
        {
            RunId = runId,
            CreatedAt = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            Status = MixLabRunStatus.Succeeded,
            Flags = new MixLabRunFlags
            {
                Genre = "techno",
                Mode = "all",
                Risk = "high",
                Directions = "mixed",
            },
            UploadId = "u_1",
            Concepts = new[]
            {
                new MixLabRunConcept
                {
                    ConceptId = "c_1",
                    Title = "107 Degrees",
                    Feedback = new MixLabConceptFeedback
                    {
                        Verdict = verdict,
                        Rating = 4,
                        RecordedAt = new DateTimeOffset(2026, 8, 16, 13, 0, 0, TimeSpan.Zero),
                    },
                },
            },
        };

        /// <summary>
        /// Only the dependencies these tests exercise are supplied; the controller's constructor
        /// performs no null checks, so the unused ones are passed as null rather than growing five
        /// stub classes that nothing asserts on.
        /// </summary>
        private static MixLabRunsController BuildSut(
            IMixLabRunQueryUseCase? query = null,
            IReindexMixLabRunsUseCase? reindex = null)
        {
            var sut = new MixLabRunsController(
                enqueue: null!,
                claim: null!,
                complete: null!,
                fail: null!,
                query: query ?? new StubRunQueryUseCase(null, Array.Empty<MixLabRunIndexEntry>()),
                artifacts: null!,
                delete: null!,
                reindex: reindex ?? new SpyReindexUseCase(returns: 0));

            sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return sut;
        }

        private sealed class StubRunQueryUseCase : IMixLabRunQueryUseCase
        {
            private readonly MixLabRun? _run;
            private readonly IReadOnlyList<MixLabRunIndexEntry> _entries;

            public StubRunQueryUseCase(MixLabRun? run, IReadOnlyList<MixLabRunIndexEntry> entries)
            {
                _run = run;
                _entries = entries;
            }

            public Task<IReadOnlyList<MixLabRunIndexEntry>> ListAsync(int? take, int? skip, CancellationToken cancellationToken) =>
                Task.FromResult(_entries);

            public Task<MixLabRun?> GetAsync(string runId, CancellationToken cancellationToken) =>
                Task.FromResult(_run);
        }

        private sealed class SpyReindexUseCase : IReindexMixLabRunsUseCase
        {
            private readonly int _returns;

            public SpyReindexUseCase(int returns)
            {
                _returns = returns;
            }

            public bool Called { get; private set; }

            public Task<int> ReindexAsync(CancellationToken cancellationToken)
            {
                Called = true;
                return Task.FromResult(_returns);
            }
        }
    }
}
