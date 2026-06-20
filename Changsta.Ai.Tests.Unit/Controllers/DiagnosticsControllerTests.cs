using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Core.Diagnostics;
using Changsta.Ai.Interface.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Changsta.Ai.Tests.Unit.Controllers
{
    // Note: bearer-secret authorisation is enforced by [BearerSecret] (an IAuthorizationFilter),
    // so it is covered by BearerSecretAttributeTests rather than at the controller level.
    [TestFixture]
    public sealed class DiagnosticsControllerTests
    {
        // ── Input validation ─────────────────────────────────────────────────
        [Test]
        public async Task GetErrorsAsync_returns_400_when_hours_is_zero()
        {
            var sut = BuildSut(new StubGetErrorInsightsUseCase());

            IActionResult result = await sut.GetErrorsAsync(0, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetErrorsAsync_returns_400_when_hours_exceeds_maximum()
        {
            var sut = BuildSut(new StubGetErrorInsightsUseCase());

            IActionResult result = await sut.GetErrorsAsync(169, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task GetErrorsAsync_accepts_boundary_hours_values()
        {
            var sut = BuildSut(new StubGetErrorInsightsUseCase());

            IActionResult resultMin = await sut.GetErrorsAsync(1, CancellationToken.None);
            IActionResult resultMax = await sut.GetErrorsAsync(168, CancellationToken.None);

            Assert.That(resultMin, Is.InstanceOf<OkObjectResult>());
            Assert.That(resultMax, Is.InstanceOf<OkObjectResult>());
        }

        // ── Response body ────────────────────────────────────────────────────
        [Test]
        public async Task GetErrorsAsync_passes_hours_to_use_case()
        {
            var stub = new StubGetErrorInsightsUseCase();
            var sut = BuildSut(stub);

            await sut.GetErrorsAsync(6, CancellationToken.None);

            Assert.That(stub.LastHours, Is.EqualTo(6));
        }

        [Test]
        public async Task GetErrorsAsync_returns_use_case_result_in_body()
        {
            var expected = new DiagnosticsResult
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                WindowHours = 24,
                Requests = new[]
                {
                    new DiagnosticsRequest
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        StatusCode = 500,
                        Name = "GET /api/recommend",
                    },
                },
            };
            var stub = new StubGetErrorInsightsUseCase(expected);
            var sut = BuildSut(stub);

            IActionResult result = await sut.GetErrorsAsync(24, CancellationToken.None);

            var ok = (OkObjectResult)result;
            Assert.That(ok.Value, Is.SameAs(expected));
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static DiagnosticsController BuildSut(IGetErrorInsightsUseCase useCase)
        {
            var controller = new DiagnosticsController(useCase);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

            return controller;
        }

        private sealed class StubGetErrorInsightsUseCase : IGetErrorInsightsUseCase
        {
            private readonly DiagnosticsResult _result;

            public StubGetErrorInsightsUseCase()
            {
                _result = new DiagnosticsResult
                {
                    GeneratedAt = DateTimeOffset.UtcNow,
                    WindowHours = 24,
                };
            }

            public StubGetErrorInsightsUseCase(DiagnosticsResult result)
            {
                _result = result;
            }

            public int LastHours { get; private set; }

            public Task<DiagnosticsResult> GetErrorsAsync(int hours, CancellationToken cancellationToken)
            {
                LastHours = hours;
                return Task.FromResult(_result);
            }
        }
    }
}
