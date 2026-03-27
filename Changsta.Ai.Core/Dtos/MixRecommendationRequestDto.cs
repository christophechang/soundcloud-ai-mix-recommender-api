using System.ComponentModel.DataAnnotations;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class MixRecommendationRequestDto
    {
        [MinLength(1)]
        [MaxLength(500)]
        required public string Question { get; init; }

        public int MaxResults { get; init; } = 3;
    }
}