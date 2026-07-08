using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.MixLab;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Returns the MixLab uploads index verbatim from <see cref="IMixLabUploadRepository"/>. See
    /// docs/architecture/mixlab-anywhere.md §4 row 2 and issue #129.
    /// </summary>
    public sealed class GetUploadsUseCase : IGetUploadsUseCase
    {
        private readonly IMixLabUploadRepository _repository;

        public GetUploadsUseCase(IMixLabUploadRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public Task<IReadOnlyList<MixLabUpload>> GetUploadsAsync(CancellationToken cancellationToken) =>
            _repository.GetIndexAsync(cancellationToken);
    }
}
