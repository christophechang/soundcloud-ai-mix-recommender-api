using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Infrastructure.Services.Ai.Recommenders
{
    internal sealed class MixRecommendationQueryAnalysis
    {
        public required Mix[] FilteredMixes { get; init; }

        public int? PureBpmQuery { get; init; }

        public string? DetectedGenre { get; init; }

        public bool IsPureGenreQuery { get; init; }

        public bool IncludeTrackTitles { get; init; }
    }
}
