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
    public sealed class MixLabFeedbackControllerTests
    {
        [Test]
        public async Task SubmitFeedbackAsync_recorded_returns_200()
        {
            var sut = BuildSut(submitResult: new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded,
            });

            IActionResult result = await sut.SubmitFeedbackAsync("r_1", "concept-1", Json("{\"verdict\":\"played\"}"), CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Test]
        public async Task SubmitFeedbackAsync_non_object_body_returns_400_without_calling_use_case()
        {
            var spy = new SpySubmitFeedbackUseCase(new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded,
            });
            var sut = BuildSut(submitUseCase: spy);

            IActionResult result = await sut.SubmitFeedbackAsync("r_1", "concept-1", Json("[]"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
            spy.Called.Should().BeFalse();
        }

        [Test]
        public async Task SubmitFeedbackAsync_passes_parsed_fields_to_use_case()
        {
            var spy = new SpySubmitFeedbackUseCase(new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded,
            });
            var sut = BuildSut(submitUseCase: spy);

            await sut.SubmitFeedbackAsync(
                "r_1",
                "concept-1",
                Json("{\"verdict\":\"played_modified\",\"rating\":4,\"notes\":\"nice\",\"publishedMixSlug\":\"my-mix\"}"),
                CancellationToken.None);

            spy.RunIdReceived.Should().Be("r_1");
            spy.ConceptIdReceived.Should().Be("concept-1");
            spy.VerdictReceived.Should().Be("played_modified");
            spy.RatingReceived.Should().Be(4);
            spy.NotesReceived.Should().Be("nice");
            spy.PublishedMixSlugReceived.Should().Be("my-mix");
        }

        [Test]
        public async Task SubmitFeedbackAsync_invalid_request_returns_400()
        {
            var sut = BuildSut(submitResult: new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.InvalidRequest,
                ErrorMessage = "bad",
            });

            IActionResult result = await sut.SubmitFeedbackAsync("r_1", "concept-1", Json("{\"rating\":9}"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task SubmitFeedbackAsync_run_not_found_returns_404()
        {
            var sut = BuildSut(submitResult: new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.RunNotFound,
            });

            IActionResult result = await sut.SubmitFeedbackAsync("r_unknown", "concept-1", Json("{\"rating\":3}"), CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task SubmitFeedbackAsync_concept_not_found_returns_404()
        {
            var sut = BuildSut(submitResult: new SubmitMixLabConceptFeedbackResult
            {
                Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.ConceptNotFound,
            });

            IActionResult result = await sut.SubmitFeedbackAsync("r_1", "does-not-exist", Json("{\"rating\":3}"), CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task GetPendingFeedbackAsync_serialises_verdict_as_snake_case_wire_value()
        {
            var events = new[]
            {
                new MixLabFeedbackEvent
                {
                    EventId = "f_1",
                    RunId = "r_1",
                    ConceptId = "concept-1",
                    Verdict = MixLabFeedbackVerdict.PlayedModified,
                    RecordedAt = DateTimeOffset.UtcNow,
                },
            };
            var sut = BuildSut(pendingEvents: events);

            IActionResult result = await sut.GetPendingFeedbackAsync(CancellationToken.None);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            string json = JsonSerializer.Serialize(ok.Value);
            json.Should().Contain("\"verdict\":\"played_modified\"");
        }

        [Test]
        public async Task AckFeedbackAsync_missing_eventIds_returns_400()
        {
            var sut = BuildSut();

            IActionResult result = await sut.AckFeedbackAsync(Json("{}"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Test]
        public async Task AckFeedbackAsync_empty_array_is_200_no_op()
        {
            var spy = new SpyAckFeedbackUseCase();
            var sut = BuildSut(ackUseCase: spy);

            IActionResult result = await sut.AckFeedbackAsync(Json("{\"eventIds\":[]}"), CancellationToken.None);

            result.Should().BeOfType<OkObjectResult>();
            spy.EventIdsReceived.Should().NotBeNull();
            spy.EventIdsReceived!.Should().BeEmpty();
        }

        [Test]
        public async Task AckFeedbackAsync_passes_event_ids_through()
        {
            var spy = new SpyAckFeedbackUseCase();
            var sut = BuildSut(ackUseCase: spy);

            await sut.AckFeedbackAsync(Json("{\"eventIds\":[\"f_1\",\"f_2\"]}"), CancellationToken.None);

            spy.EventIdsReceived.Should().BeEquivalentTo(new[] { "f_1", "f_2" });
        }

        [Test]
        public void Route_is_api_mixlab()
        {
            var routeAttribute = typeof(MixLabFeedbackController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>()
                .Single();

            routeAttribute.Template.Should().Be("api/mixlab");
        }

        [Test]
        public void Controller_requires_BearerSecret_for_MixLab_ApiSecret()
        {
            var bearerSecretAttribute = typeof(MixLabFeedbackController)
                .GetCustomAttributes(typeof(BearerSecretAttribute), inherit: false)
                .Cast<BearerSecretAttribute>()
                .Single();

            GetConfigurationKey(bearerSecretAttribute).Should().Be("MixLab:ApiSecret");
        }

        private static string GetConfigurationKey(BearerSecretAttribute attribute)
        {
            return (string)typeof(BearerSecretAttribute)
                .GetField("_configurationKey", BindingFlags.NonPublic | BindingFlags.Instance) !
                .GetValue(attribute) !;
        }

        private static JsonElement Json(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static MixLabFeedbackController BuildSut(
            ISubmitMixLabConceptFeedbackUseCase? submitUseCase = null,
            SubmitMixLabConceptFeedbackResult? submitResult = null,
            IReadOnlyList<MixLabFeedbackEvent>? pendingEvents = null,
            IAckMixLabFeedbackUseCase? ackUseCase = null)
        {
            var sut = new MixLabFeedbackController(
                submitUseCase ?? new StubSubmitFeedbackUseCase(submitResult ?? new SubmitMixLabConceptFeedbackResult
                {
                    Outcome = SubmitMixLabConceptFeedbackResult.SubmitOutcome.Recorded,
                }),
                new StubGetPendingFeedbackUseCase(pendingEvents ?? Array.Empty<MixLabFeedbackEvent>()),
                ackUseCase ?? new SpyAckFeedbackUseCase());

            sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return sut;
        }

        private sealed class StubSubmitFeedbackUseCase : ISubmitMixLabConceptFeedbackUseCase
        {
            private readonly SubmitMixLabConceptFeedbackResult _result;

            public StubSubmitFeedbackUseCase(SubmitMixLabConceptFeedbackResult result)
            {
                _result = result;
            }

            public Task<SubmitMixLabConceptFeedbackResult> SubmitAsync(
                string runId,
                string conceptId,
                string? verdict,
                int? rating,
                string? notes,
                string? publishedMixSlug,
                CancellationToken cancellationToken) => Task.FromResult(_result);
        }

        private sealed class SpySubmitFeedbackUseCase : ISubmitMixLabConceptFeedbackUseCase
        {
            private readonly SubmitMixLabConceptFeedbackResult _result;

            public SpySubmitFeedbackUseCase(SubmitMixLabConceptFeedbackResult result)
            {
                _result = result;
            }

            public bool Called { get; private set; }

            public string? RunIdReceived { get; private set; }

            public string? ConceptIdReceived { get; private set; }

            public string? VerdictReceived { get; private set; }

            public int? RatingReceived { get; private set; }

            public string? NotesReceived { get; private set; }

            public string? PublishedMixSlugReceived { get; private set; }

            public Task<SubmitMixLabConceptFeedbackResult> SubmitAsync(
                string runId,
                string conceptId,
                string? verdict,
                int? rating,
                string? notes,
                string? publishedMixSlug,
                CancellationToken cancellationToken)
            {
                Called = true;
                RunIdReceived = runId;
                ConceptIdReceived = conceptId;
                VerdictReceived = verdict;
                RatingReceived = rating;
                NotesReceived = notes;
                PublishedMixSlugReceived = publishedMixSlug;
                return Task.FromResult(_result);
            }
        }

        private sealed class StubGetPendingFeedbackUseCase : IGetPendingMixLabFeedbackUseCase
        {
            private readonly IReadOnlyList<MixLabFeedbackEvent> _events;

            public StubGetPendingFeedbackUseCase(IReadOnlyList<MixLabFeedbackEvent> events)
            {
                _events = events;
            }

            public Task<IReadOnlyList<MixLabFeedbackEvent>> GetPendingAsync(CancellationToken cancellationToken) =>
                Task.FromResult(_events);
        }

        private sealed class SpyAckFeedbackUseCase : IAckMixLabFeedbackUseCase
        {
            public IReadOnlyList<string>? EventIdsReceived { get; private set; }

            public Task AckAsync(IReadOnlyList<string> eventIds, CancellationToken cancellationToken)
            {
                EventIdsReceived = eventIds;
                return Task.CompletedTask;
            }
        }
    }
}
