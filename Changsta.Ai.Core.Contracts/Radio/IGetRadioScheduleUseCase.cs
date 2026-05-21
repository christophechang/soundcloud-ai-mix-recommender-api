using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Dtos;

namespace Changsta.Ai.Core.Contracts.Radio
{
    public interface IGetRadioScheduleUseCase
    {
        Task<RadioScheduleResultDto> GetAsync(CancellationToken cancellationToken);
    }
}
