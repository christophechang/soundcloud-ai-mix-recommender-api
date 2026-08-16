using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.Exceptions;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    /// <summary>
    /// In-memory fake for the internal <c>IMixLabBlobGateway</c> seam (accessible here via
    /// InternalsVisibleTo on Changsta.Ai.Infrastructure.Services.Azure). Lets the MixLab
    /// repositories' claim/complete/prune/retry logic be exercised deterministically, including
    /// simulated ETag conflicts, without the real Azure SDK. See issue #128.
    /// </summary>
    internal sealed class FakeMixLabBlobGateway : IMixLabBlobGateway
    {
        private readonly Dictionary<string, (byte[] Content, string ETag)> _blobs = new(StringComparer.Ordinal);
        private int _nextETag = 1;

        /// <summary>
        /// When greater than zero, the next <see cref="WriteAsync"/> calls throw a simulated
        /// <see cref="MixLabConcurrencyException"/> instead of writing, decrementing this count
        /// each time — used to test bounded retry behaviour.
        /// </summary>
        public int ForcedConflictsRemaining { get; set; }

        /// <summary>
        /// Path-scoped equivalent of <see cref="ForcedConflictsRemaining"/>: the next N
        /// <see cref="WriteAsync"/> calls for that exact blob path throw a simulated
        /// <see cref="MixLabConcurrencyException"/>, whether or not they carry an expected ETag —
        /// like the global counter, this one is spent before any precondition is inspected.
        /// (<see cref="WriteUnconditionalAsync"/> consults neither counter.) Needed because an
        /// operation that writes the manifest and then the index would otherwise spend the global
        /// counter on the manifest, leaving the index's retry loop untested.
        /// </summary>
        public Dictionary<string, int> ForcedConflictsByPath { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Every blob path passed to <see cref="ReadAsync"/>, in order — lets a test assert that a
        /// retrying index mutation re-read the run manifest on each attempt rather than reusing a
        /// stale snapshot.
        /// </summary>
        public List<string> ReadPaths { get; } = new();

        public List<string> DeletedPaths { get; } = new();

        public List<string> WrittenPaths { get; } = new();

        public Task<MixLabBlobReadResult?> ReadAsync(string blobPath, CancellationToken cancellationToken)
        {
            ReadPaths.Add(blobPath);

            if (_blobs.TryGetValue(blobPath, out var stored))
            {
                return Task.FromResult<MixLabBlobReadResult?>(new MixLabBlobReadResult(stored.Content, stored.ETag));
            }

            return Task.FromResult<MixLabBlobReadResult?>(null);
        }

        public Task<string> WriteAsync(
            string blobPath,
            ReadOnlyMemory<byte> content,
            string? expectedETag,
            CancellationToken cancellationToken)
        {
            if (ForcedConflictsByPath.TryGetValue(blobPath, out int pathConflicts) && pathConflicts > 0)
            {
                ForcedConflictsByPath[blobPath] = pathConflicts - 1;
                throw new MixLabConcurrencyException($"Simulated MixLab blob write conflict for '{blobPath}'.");
            }

            if (ForcedConflictsRemaining > 0)
            {
                ForcedConflictsRemaining--;
                throw new MixLabConcurrencyException("Simulated MixLab blob write conflict.");
            }

            bool exists = _blobs.TryGetValue(blobPath, out var stored);

            if (expectedETag is null)
            {
                if (exists)
                {
                    throw new MixLabConcurrencyException(
                        $"Simulated create-only conflict: blob '{blobPath}' already exists.");
                }
            }
            else if (!exists || !string.Equals(stored.ETag, expectedETag, StringComparison.Ordinal))
            {
                throw new MixLabConcurrencyException(
                    $"Simulated If-Match conflict: blob '{blobPath}' changed since it was read.");
            }

            string newETag = "etag-" + _nextETag++;
            _blobs[blobPath] = (content.ToArray(), newETag);
            WrittenPaths.Add(blobPath);
            return Task.FromResult(newETag);
        }

        /// <summary>
        /// Unconditional overwrite. Deliberately does not consult
        /// <see cref="ForcedConflictsRemaining"/> — unconditional writes have no precondition to
        /// fail, so simulated conflicts must not be spent here (see
        /// <see cref="MixLabBlobGateway.WriteUnconditionalAsync"/>).
        /// </summary>
        public Task<string> WriteUnconditionalAsync(
            string blobPath,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            string newETag = "etag-" + _nextETag++;
            _blobs[blobPath] = (content.ToArray(), newETag);
            WrittenPaths.Add(blobPath);
            return Task.FromResult(newETag);
        }

        public Task<Stream> OpenReadStreamAsync(string blobPath, CancellationToken cancellationToken)
        {
            if (!_blobs.TryGetValue(blobPath, out var stored))
            {
                throw new InvalidOperationException($"Blob '{blobPath}' not found.");
            }

            return Task.FromResult<Stream>(new MemoryStream(stored.Content));
        }

        public Task WriteStreamAsync(string blobPath, Stream content, CancellationToken cancellationToken)
        {
            if (_blobs.ContainsKey(blobPath))
            {
                throw new MixLabConcurrencyException($"Blob '{blobPath}' already exists and is immutable.");
            }

            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            string newETag = "etag-" + _nextETag++;
            _blobs[blobPath] = (buffer.ToArray(), newETag);
            WrittenPaths.Add(blobPath);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(_blobs.ContainsKey(blobPath));
        }

        public Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
        {
            if (_blobs.Remove(blobPath))
            {
                DeletedPaths.Add(blobPath);
            }

            return Task.CompletedTask;
        }
    }
}
