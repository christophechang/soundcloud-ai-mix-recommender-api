using System.IO;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// The resolved stream for a stored upload plus the concrete upload id it resolved to (relevant
    /// when the request used the literal id <c>latest</c>). See
    /// docs/architecture/mixlab-anywhere.md §4 row 3 and issue #129.
    /// </summary>
    public sealed class MixLabUploadContent
    {
        public MixLabUploadContent(string uploadId, Stream content)
        {
            UploadId = uploadId;
            Content = content;
        }

        public string UploadId { get; }

        public Stream Content { get; }
    }
}
