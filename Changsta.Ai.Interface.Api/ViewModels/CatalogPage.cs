namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class CatalogPage<T>
    {
        required public T[] Items { get; init; }

        required public int Total { get; init; }

        required public int Page { get; init; }

        required public int PageSize { get; init; }

        required public int TotalPages { get; init; }
    }
}
