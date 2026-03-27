using System;
using System.Collections.Generic;
using System.Text;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class MixRecommendationResponseDto
    {
        required public IReadOnlyList<MixRecommendationResultDto> Results { get; init; }

        public string? ClarifyingQuestion { get; init; }

        public int? MaxResultsApplied { get; init; }
    }
}
