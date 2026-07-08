using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;

namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>
    /// Blob-backed <see cref="IMixLabArtifactStore"/> for the immutable per-run artifacts
    /// (<c>report.html</c>, <c>export.xml</c>, <c>summary.json</c>). See
    /// docs/architecture/mixlab-anywhere.md §3.
    /// </summary>
    internal sealed class BlobMixLabArtifactStore : IMixLabArtifactStore
    {
        private readonly IMixLabBlobGateway _gateway;

        public BlobMixLabArtifactStore(IMixLabBlobGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public Task SaveAsync(string runId, string name, Stream content, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(content);

            return _gateway.WriteStreamAsync(MixLabBlobPaths.RunArtifact(runId, name), content, cancellationToken);
        }

        public Task<Stream> OpenReadAsync(string runId, string name, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return _gateway.OpenReadStreamAsync(MixLabBlobPaths.RunArtifact(runId, name), cancellationToken);
        }
    }
}
