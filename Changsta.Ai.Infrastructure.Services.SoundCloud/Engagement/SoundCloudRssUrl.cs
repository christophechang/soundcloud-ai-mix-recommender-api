using System.Globalization;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Engagement
{
    /// <summary>
    /// Derives the numeric SoundCloud user id from the configured RSS feed URL
    /// (<c>…/users/soundcloud:users:&lt;digits&gt;/sounds.rss</c>), so no separate config key is needed.
    /// </summary>
    internal static class SoundCloudRssUrl
    {
        private const string Marker = "soundcloud:users:";

        public static bool TryParseUserId(string? rssUrl, out long userId)
        {
            userId = 0;

            if (string.IsNullOrWhiteSpace(rssUrl))
            {
                return false;
            }

            int start = rssUrl.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return false;
            }

            start += Marker.Length;
            int end = start;
            while (end < rssUrl.Length && char.IsAsciiDigit(rssUrl[end]))
            {
                end++;
            }

            if (end == start || (end < rssUrl.Length && rssUrl[end] != '/'))
            {
                return false;
            }

            if (!long.TryParse(rssUrl.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
                || parsed <= 0)
            {
                return false;
            }

            userId = parsed;
            return true;
        }
    }
}
