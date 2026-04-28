namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class GenreEntry
    {
        required public string Genre { get; init; }

        required public ArtistEntry[] Artists { get; init; }
    }
}
