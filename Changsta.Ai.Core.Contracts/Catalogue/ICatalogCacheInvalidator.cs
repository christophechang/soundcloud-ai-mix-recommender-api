namespace Changsta.Ai.Core.Contracts.Catalogue
{
    public interface ICatalogCacheInvalidator
    {
        int Version { get; }

        void Invalidate();
    }
}
