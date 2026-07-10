using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Interface.Api.Controllers;
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
        public async Task GetExportAsync_missing_export_on_known_run_returns_204()
        {
            var sut = BuildSut(artifactStatus: MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound);

            IActionResult result = await sut.GetExportAsync("r_1", CancellationToken.None);

            result.Should().BeOfType<NoContentResult>();
        }

        [Test]
        public async Task GetReportAsync_missing_report_returns_404()
        {
            var sut = BuildSut(artifactStatus: MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound);

            IActionResult result = await sut.GetReportAsync("r_1", CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Test]
        public async Task GetExportAsync_unknown_run_returns_404()
        {
            var sut = BuildSut(artifactStatus: MixLabRunArtifactResult.ArtifactStatus.RunNotFound);

            IActionResult result = await sut.GetExportAsync("r_unknown", CancellationToken.None);

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        private static MixLabRunsController BuildSut(MixLabRunArtifactResult.ArtifactStatus artifactStatus)
        {
            var sut = new MixLabRunsController(
                new StubEnqueueMixLabRunUseCase(),
                new StubClaimMixLabRunUseCase(),
                new StubCompleteMixLabRunUseCase(),
                new StubFailMixLabRunUseCase(),
                new StubMixLabRunQueryUseCase(),
                new StubOpenMixLabRunArtifactUseCase(artifactStatus));

            sut.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return sut;
        }

        private sealed class StubOpenMixLabRunArtifactUseCase : IOpenMixLabRunArtifactUseCase
        {
            private readonly MixLabRunArtifactResult.ArtifactStatus _status;

            public StubOpenMixLabRunArtifactUseCase(MixLabRunArtifactResult.ArtifactStatus status)
            {
                _status = status;
            }

            public Task<MixLabRunArtifactResult> OpenAsync(
                string runId,
                MixLabRunArtifactKind kind,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new MixLabRunArtifactResult { Status = _status });
            }
        }

        private sealed class StubEnqueueMixLabRunUseCase : IEnqueueMixLabRunUseCase
        {
            public Task<EnqueueMixLabRunResult> EnqueueAsync(
                System.Text.Json.JsonElement flags,
                string? uploadId,
                CancellationToken cancellationToken) =>
                Task.FromResult(new EnqueueMixLabRunResult { Outcome = EnqueueMixLabRunResult.EnqueueOutcome.Created, RunId = "r_1" });
        }

        private sealed class StubClaimMixLabRunUseCase : IClaimMixLabRunUseCase
        {
            public Task<MixLabRun?> ClaimAsync(string workerId, CancellationToken cancellationToken) =>
                Task.FromResult<MixLabRun?>(null);
        }

        private sealed class StubCompleteMixLabRunUseCase : ICompleteMixLabRunUseCase
        {
            public Task<CompleteMixLabRunResult> CompleteAsync(
                string runId,
                Stream summary,
                Stream report,
                Stream? export,
                CancellationToken cancellationToken) =>
                Task.FromResult(new CompleteMixLabRunResult { Outcome = CompleteMixLabRunResult.CompleteOutcome.Completed });
        }

        private sealed class StubFailMixLabRunUseCase : IFailMixLabRunUseCase
        {
            public Task<FailMixLabRunResult> FailAsync(
                string runId,
                string error,
                string? logTail,
                CancellationToken cancellationToken) =>
                Task.FromResult(new FailMixLabRunResult { Outcome = FailMixLabRunResult.FailOutcome.Failed });
        }

        private sealed class StubMixLabRunQueryUseCase : IMixLabRunQueryUseCase
        {
            public Task<IReadOnlyList<MixLabRunIndexEntry>> ListAsync(
                int? take,
                int? skip,
                CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<MixLabRunIndexEntry>>(Array.Empty<MixLabRunIndexEntry>());

            public Task<MixLabRun?> GetAsync(string runId, CancellationToken cancellationToken) =>
                Task.FromResult<MixLabRun?>(null);
        }
    }
}
