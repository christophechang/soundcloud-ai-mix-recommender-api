using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Resolves the target upload (a concrete id or the literal <c>latest</c>) exactly like
    /// <see cref="EnqueueMixLabRunUseCase"/> does for runs, then requests (or refreshes) the
    /// upload's library-map job. See issue #128.
    /// </summary>
    public sealed class RequestMixLabMapUseCase : IRequestMixLabMapUseCase
    {
        private const string LatestUpload = "latest";

        private readonly IMixLabMapRepository _maps;
        private readonly IMixLabUploadRepository _uploads;

        public RequestMixLabMapUseCase(IMixLabMapRepository maps, IMixLabUploadRepository uploads)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        }

        public async Task<RequestMixLabMapResult> RequestAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            string resolvedUploadId;
            if (string.Equals(uploadId, LatestUpload, StringComparison.Ordinal))
            {
                string? latest = await _uploads.GetLatestIdAsync(cancellationToken).ConfigureAwait(false);
                if (latest is null)
                {
                    return new RequestMixLabMapResult
                    {
                        Outcome = RequestMixLabMapResult.RequestOutcome.NoUploadsAvailable,
                    };
                }

                resolvedUploadId = latest;
            }
            else
            {
                if (!await UploadExistsAsync(uploadId, cancellationToken).ConfigureAwait(false))
                {
                    return new RequestMixLabMapResult
                    {
                        Outcome = RequestMixLabMapResult.RequestOutcome.UnknownUpload,
                    };
                }

                resolvedUploadId = uploadId;
            }

            MixLabMapJob job = await _maps.RequestAsync(resolvedUploadId, cancellationToken).ConfigureAwait(false);

            return new RequestMixLabMapResult
            {
                Outcome = RequestMixLabMapResult.RequestOutcome.Accepted,
                Job = job,
            };
        }

        private async Task<bool> UploadExistsAsync(string uploadId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MixLabUpload> uploads = await _uploads.GetIndexAsync(cancellationToken).ConfigureAwait(false);
            foreach (MixLabUpload upload in uploads)
            {
                if (string.Equals(upload.UploadId, uploadId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
