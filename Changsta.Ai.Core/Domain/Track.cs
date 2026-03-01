using System;

namespace Changsta.Ai.Core.Domain
{
    public sealed class Track : IEquatable<Track>
    {
        required public string Artist { get; init; }

        required public string Title { get; init; }

        public bool Equals(Track? other)
        {
            if (other is null)
            {
                return false;
            }

            return string.Equals(Artist, other.Artist, StringComparison.Ordinal)
                && string.Equals(Title, other.Title, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as Track);

        public override int GetHashCode() => HashCode.Combine(Artist, Title);
    }
}
