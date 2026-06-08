using System.Text;

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

        public static string FromTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(title.Length);
            bool previousWasSeparator = true;

            foreach (char ch in title.Trim())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                    previousWasSeparator = false;
                    continue;
                }

                if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    previousWasSeparator = true;
                }
            }

            if (builder.Length > 0 && builder[builder.Length - 1] == '-')
            {
                builder.Length--;
            }

            return builder.ToString();
        }
    }
}
