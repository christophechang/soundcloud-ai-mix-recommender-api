using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class MixRecommendationResultDto
    {
        // stable id we define (can be SoundCloud GUID or derived)
        required public string MixId { get; init; }

        required public string Title { get; init; }

        required public string Url { get; init; }

        required public string Reason { get; init; }

        required public IReadOnlyList<string> Why { get; init; }

        // 0..1
        public double Confidence { get; init; }
    }
}