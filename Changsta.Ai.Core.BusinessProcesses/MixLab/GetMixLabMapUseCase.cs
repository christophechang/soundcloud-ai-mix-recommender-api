using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Resolves the target upload exactly like <see cref="RequestMixLabMapUseCase"/> does, then
    /// reads its library-map job and, when the job has succeeded, the stored engine payload. See
    /// issue #128.
    /// </summary>
    public sealed class GetMixLabMapUseCase : IGetMixLabMapUseCase
    {
        private const string LatestUpload = "latest";

        private readonly IMixLabMapRepository _maps;
        private readonly IMixLabUploadRepository _uploads;

        public GetMixLabMapUseCase(IMixLabMapRepository maps, IMixLabUploadRepository uploads)
        {
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));
            _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        }

        public async Task<GetMixLabMapResult> GetAsync(string uploadId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

            string resolvedUploadId;
            if (string.Equals(uploadId, LatestUpload, StringComparison.Ordinal))
            {
                string? latest = await _uploads.GetLatestIdAsync(cancellationToken).ConfigureAwait(false);
                if (latest is null)
                {
                    return NotFound();
                }

                resolvedUploadId = latest;
            }
            else
            {
                if (!await UploadExistsAsync(uploadId, cancellationToken).ConfigureAwait(false))
                {
                    return NotFound();
                }

                resolvedUploadId = uploadId;
            }

            MixLabMapJob? job = await _maps.GetJobAsync(resolvedUploadId, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                return NotFound();
            }

            byte[]? payload = job.Status == MixLabMapStatus.Succeeded
                ? await _maps.OpenPayloadAsync(resolvedUploadId, cancellationToken).ConfigureAwait(false)
                : null;

            return new GetMixLabMapResult
            {
                Outcome = GetMixLabMapResult.GetOutcome.Found,
                Job = job,
                Payload = payload,
            };
        }

        private static GetMixLabMapResult NotFound()
        {
            return new GetMixLabMapResult { Outcome = GetMixLabMapResult.GetOutcome.NotFound };
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
