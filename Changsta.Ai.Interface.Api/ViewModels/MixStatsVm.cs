namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class MixStatsVm
    {
        required public string Id { get; init; }

        public long? Plays { get; init; }

        public long? Likes { get; init; }
    }
}
