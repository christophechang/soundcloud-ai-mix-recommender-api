using System;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Contracts.Diagnostics;
using Changsta.Ai.Core.Diagnostics;

namespace Changsta.Ai.Core.BusinessProcesses.Diagnostics
{
    public sealed class GetErrorInsightsUseCase : IGetErrorInsightsUseCase
    {
        private readonly IErrorInsightsProvider _provider;

        public GetErrorInsightsUseCase(IErrorInsightsProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public Task<DiagnosticsResult> GetErrorsAsync(int hours, CancellationToken cancellationToken)
        {
            return _provider.GetErrorsAsync(hours, cancellationToken);
        }
    }
}
