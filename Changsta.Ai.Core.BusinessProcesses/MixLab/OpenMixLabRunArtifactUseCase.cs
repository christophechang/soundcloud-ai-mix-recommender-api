using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Resolves a run artifact for streaming: distinguishes an unknown run from a missing artifact.
    /// Artifacts exist only once a run has succeeded, so any request before success is reported as a
    /// missing artifact. The export is optional even for a succeeded run. See issue #130.
    /// </summary>
    public sealed class OpenMixLabRunArtifactUseCase : IOpenMixLabRunArtifactUseCase
    {
        private const string ReportArtifact = "report.html";

        private const string ExportArtifact = "export.xml";

        private const string SummaryArtifact = "summary.json";

        private readonly IMixLabRunRepository _runs;
        private readonly IMixLabArtifactStore _artifacts;

        public OpenMixLabRunArtifactUseCase(IMixLabRunRepository runs, IMixLabArtifactStore artifacts)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        }

        public async Task<MixLabRunArtifactResult> OpenAsync(string runId, MixLabRunArtifactKind kind, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);

            MixLabRun? run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
            if (run is null)
            {
                return Status(MixLabRunArtifactResult.ArtifactStatus.RunNotFound);
            }

            if (run.Status != MixLabRunStatus.Succeeded)
            {
                // report.html/summary.json/export.xml are written only at completion.
                return Status(MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound);
            }

            (string name, string contentType, bool optional) = Describe(kind);

            if (!optional)
            {
                Stream required = await _artifacts.OpenReadAsync(runId, name, cancellationToken).ConfigureAwait(false);
                return Found(required, contentType);
            }

            // The export is optional, and IMixLabArtifactStore exposes no existence probe and never
            // returns null (see risk note on issue #130); a missing optional export surfaces as a
            // throw from the store, which we map to artifact-not-found.
            try
            {
                Stream export = await _artifacts.OpenReadAsync(runId, name, cancellationToken).ConfigureAwait(false);
                return Found(export, contentType);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Status(MixLabRunArtifactResult.ArtifactStatus.ArtifactNotFound);
            }
        }

        private static (string Name, string ContentType, bool Optional) Describe(MixLabRunArtifactKind kind)
        {
            return kind switch
            {
                MixLabRunArtifactKind.Report => (ReportArtifact, "text/html", false),
                MixLabRunArtifactKind.Summary => (SummaryArtifact, "application/json", false),
                MixLabRunArtifactKind.Export => (ExportArtifact, "application/xml", true),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown MixLab artifact kind."),
            };
        }

        private static MixLabRunArtifactResult Status(MixLabRunArtifactResult.ArtifactStatus status)
        {
            return new MixLabRunArtifactResult { Status = status };
        }

        private static MixLabRunArtifactResult Found(Stream content, string contentType)
        {
            return new MixLabRunArtifactResult
            {
                Status = MixLabRunArtifactResult.ArtifactStatus.Found,
                Content = content,
                ContentType = contentType,
            };
        }
    }
}
