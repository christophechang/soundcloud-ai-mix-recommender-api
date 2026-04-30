namespace Changsta.Ai.Core.Normalization
{
    public static class MixSlugHelper
    {
        public static string ExtractSlug(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            ReadOnlySpan<char> span = url.AsSpan().TrimEnd('/');
            int lastSlash = span.LastIndexOf('/');
            return lastSlash < 0 ? string.Empty : span[(lastSlash + 1) ..].ToString();
        }
    }
}
