using System;

namespace Changsta.Ai.Core.Exceptions
{
    /// <summary>
    /// Thrown when a blob catalogue write is rejected because the underlying blob changed since it
    /// was read (optimistic-concurrency / If-Match precondition failure). Callers performing a
    /// read-modify-write may re-read and retry a bounded number of times. See issue #34.
    /// </summary>
    public sealed class CatalogConcurrencyException : Exception
    {
        public CatalogConcurrencyException(string message)
            : base(message)
        {
        }

        public CatalogConcurrencyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
