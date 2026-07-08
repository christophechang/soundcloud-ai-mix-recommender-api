using System;

namespace Changsta.Ai.Core.Exceptions
{
    /// <summary>
    /// Thrown when a MixLab blob write (an index or a run manifest) is rejected because the
    /// underlying blob changed since it was read (optimistic-concurrency / If-Match precondition
    /// failure). Callers performing a read-modify-write may re-read and retry a bounded number of
    /// times. Sibling of <see cref="CatalogConcurrencyException"/>, kept separate so MixLab and
    /// catalogue concurrency failures are distinguishable to callers. See
    /// docs/architecture/mixlab-anywhere.md §3 and issue #128.
    /// </summary>
    public sealed class MixLabConcurrencyException : Exception
    {
        public MixLabConcurrencyException(string message)
            : base(message)
        {
        }

        public MixLabConcurrencyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
