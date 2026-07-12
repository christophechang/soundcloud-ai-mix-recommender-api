namespace Changsta.Ai.Infrastructure.Services.Azure.MixLab
{
    /// <summary>The raw bytes and ETag read back from a MixLab blob.</summary>
    internal sealed class MixLabBlobReadResult
    {
        public MixLabBlobReadResult(byte[] content, string eTag)
        {
            Content = content;
            ETag = eTag;
        }

        public byte[] Content { get; }

        public string ETag { get; }
    }
}
