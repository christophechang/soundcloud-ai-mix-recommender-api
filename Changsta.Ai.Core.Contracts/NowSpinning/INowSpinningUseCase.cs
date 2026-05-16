using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.NowSpinning
{
    public interface INowSpinningUseCase
    {
        Task<NowSpinningResultDto> GetAsync(NowSpinningRequestDto request, CancellationToken cancellationToken);
    }
}
