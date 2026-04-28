namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class ArtistEntry
    {
        required public string Name { get; init; }

        required public string[] Tracks { get; init; }
    }
}
