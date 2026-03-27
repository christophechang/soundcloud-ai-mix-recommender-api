using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class MixRecommendationRequestDto
    {
        [MaxLength(500)]
        required public string Question { get; init; }

        public int MaxResults { get; init; } = 3;
    }
}