using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Diagnostics;

namespace Changsta.Ai.Core.Contracts.Diagnostics
{
    public interface IErrorInsightsProvider
    {
        Task<DiagnosticsResult> GetErrorsAsync(int hours, CancellationToken cancellationToken);
    }
}
