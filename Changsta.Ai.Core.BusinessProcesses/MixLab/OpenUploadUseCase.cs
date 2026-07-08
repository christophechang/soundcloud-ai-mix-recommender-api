using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Resolves an upload id (including the literal <c>latest</c>) against the uploads index and
    /// opens the stored gzip stream. See docs/architecture/mixlab-anywhere.md §4 row 3 and issue
    /// #129.
    /// </summary>
    public sealed class OpenUploadUseCase : IOpenUploadUseCase
    {
        private const string LatestUploadId = "latest";

        private readonly IMixLabUploadRepository _repository;

        public OpenUploadUseCase(IMixLabUploadRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<MixLabUploadContent?> OpenAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            string? resolvedId = string.Equals(uploadId, LatestUploadId, StringComparison.Ordinal)
                ? await _repository.GetLatestIdAsync(cancellationToken).ConfigureAwait(false)
                : await ResolveKnownIdAsync(uploadId, cancellationToken).ConfigureAwait(false);

            if (resolvedId is null)
            {
                return null;
            }

            Stream content = await _repository.OpenReadAsync(resolvedId, cancellationToken).ConfigureAwait(false);
            return new MixLabUploadContent(resolvedId, content);
        }

        private async Task<string?> ResolveKnownIdAsync(string uploadId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabUpload> index = await _repository.GetIndexAsync(cancellationToken).ConfigureAwait(false);
            return index.Any(upload => string.Equals(upload.UploadId, uploadId, StringComparison.Ordinal))
                ? uploadId
                : null;
        }
    }
}
