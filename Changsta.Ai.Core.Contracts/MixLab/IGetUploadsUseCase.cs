using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Returns the MixLab uploads index. See docs/architecture/mixlab-anywhere.md §4 row 2 and
    /// issue #129.
    /// </summary>
    public interface IGetUploadsUseCase
    {
        Task<IReadOnlyList<MixLabUpload>> GetUploadsAsync(CancellationToken cancellationToken);
    }
}
