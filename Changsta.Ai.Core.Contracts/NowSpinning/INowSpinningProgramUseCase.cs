using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.NowSpinning
{
    public interface INowSpinningProgramUseCase
    {
        Task<NowSpinningProgramResultDto> GetAsync(NowSpinningProgramRequestDto request, CancellationToken cancellationToken);
    }
}
