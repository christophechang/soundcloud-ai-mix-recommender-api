using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class OpenMixLabRunArtifactUseCaseTests
    {
        [Test]
        public async Task OpenAsync_report_on_succeeded_run_streams_html()
        {
            (OpenMixLabRunArtifactUseCase sut, BlobMixLabRunRepository runs, BlobMixLabArtifactStore artifacts) = BuildSut();
            string runId = await SucceededRunAsync(runs, artifacts, withExport: false);

            MixLabRunArtifactResult result = await sut.OpenAsync(runId, MixLabRunArtifactKind.Report, CancellationToken.None);

            result.Status.Should().Be(MixLabRunArtifactResult.ArtifactStatus.Found);
            result.ContentType.Should().Be("text/html");
            (await ReadAll(result.Content!)).Should().Be("<html/>");
        }

        [Test]
        public async Task OpenAsync_summary_on_succeeded_run_streams_json()
        {
            (OpenMixLabRunArtifactUseCase sut, BlobMixLabRunRepository runs, BlobMixLabArtifactStore artifacts) = BuildSut();
            string runId = await SucceededRunAsync(runs, artifacts, withExport: false);

            MixLabRunArtifactResult result = await sut.OpenAsync(runId, MixLabRunArtifactKind.Summary, CancellationToken.None);

            result.Status.Should().Be(MixLabRunArtifactResult.ArtifactStatus.Found);
            result.ContentType.Should().Be("application/json");
        }

        [Test]
        public async Task OpenAsync_export_present_streams_xml()
        {
            (OpenMixLabRunArtifactUseCase sut, BlobMixLabRunRepository runs, BlobMixLabArtifactStore artifacts) = BuildSut();
            string runId = await SucceededRunAsync(runs, artifacts, withExport: true);

            MixLabRunArtifactResult result = await sut.OpenAsync(runId, MixLabRunArtifactKind.Export, CancellationToken.None);

            result.Status.Should().Be(MixLabRunArtifactResult.ArtifactStatus.Found);
            result.ContentType.Should().Be("application/xml");
        }

        [Test]
        public async Task OpenAsync_export_absent_on_succeeded_run_is_artifact_not_found()
        {
            (OpenMixLabRunArtifactUseCase sut, BlobMixLabRunRepository runs, BlobMixLabArtifactStore artifacts) = BuildSut();
            string runId = await SucceededRunAsync(runs, artifacts, withExport: false);

            MixLabRunArtifactResult result = await sut.OpenAsync(runId, MixLabRunArtifactKind.Export, CancellationToken.None);

            result.Status.Should().Be(MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound);
        }

        [Test]
        public async Task OpenAsync_before_completion_is_artifact_not_found()
        {
            (OpenMixLabRunArtifactUseCase sut, BlobMixLabRunRepository runs, _) = BuildSut();
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await runs.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            MixLabRunArtifactResult result = await sut.OpenAsync(created.RunId, MixLabRunArtifactKind.Report, CancellationToken.None);

            result.Status.Should().Be(MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound);
        }

        [Test]
        public async Task OpenAsync_unknown_run_is_run_not_found()
        {
            (OpenMixLabRunArtifactUseCase sut, _, _) = BuildSut();

            MixLabRunArtifactResult result = await sut.OpenAsync("r_unknown", MixLabRunArtifactKind.Report, CancellationToken.None);

            result.Status.Should().Be(MixLabRunArtifactResult.ArtifactStatus.RunNotFound);
        }

        private static (OpenMixLabRunArtifactUseCase Sut, BlobMixLabRunRepository Runs, BlobMixLabArtifactStore Artifacts) BuildSut()
        {
            var gateway = new FakeMixLabBlobGateway();
            var time = new FakeTimeProvider();
            var runs = new BlobMixLabRunRepository(gateway, time, NullLogger<BlobMixLabRunRepository>.Instance);
            var artifacts = new BlobMixLabArtifactStore(gateway);
            var sut = new OpenMixLabRunArtifactUseCase(runs, artifacts);
            return (sut, runs, artifacts);
        }

        private static async Task<string> SucceededRunAsync(
            BlobMixLabRunRepository runs,
            BlobMixLabArtifactStore artifacts,
            bool withExport)
        {
            MixLabRun created = await runs.CreateQueuedAsync(MakeFlags(), "u_1", CancellationToken.None);
            await runs.TryClaimOldestQueuedAsync("worker-1", TimeSpan.FromMinutes(45), CancellationToken.None);

            await artifacts.SaveAsync(created.RunId, "report.html", Text("<html/>"), CancellationToken.None);
            await artifacts.SaveAsync(created.RunId, "summary.json", Text("{}"), CancellationToken.None);
            if (withExport)
            {
                await artifacts.SaveAsync(created.RunId, "export.xml", Text("<DJ_PLAYLISTS/>"), CancellationToken.None);
            }

            await runs.CompleteAsync(created.RunId, Array.Empty<MixLabRunConcept>(), CancellationToken.None);
            return created.RunId;
        }

        private static async Task<string> ReadAll(Stream stream)
        {
            await using (stream)
            {
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
        }

        private static MixLabRunFlags MakeFlags()
        {
            return new MixLabRunFlags
            {
                Genre = "techno",
                Mode = "all",
                Risk = "high",
                Directions = "mixed",
            };
        }

        private static MemoryStream Text(string content)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }
    }
}
