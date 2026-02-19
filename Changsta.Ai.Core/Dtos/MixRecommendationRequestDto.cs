using System;
using System.Collections.Generic;
using System.Text;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class MixRecommendationRequestDto
    {
        required public string Question { get; init; }

        public int MaxResults { get; init; } = 3;
    }
}