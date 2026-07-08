using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Infrastructure.Services.Azure.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class BlobMixLabArtifactStoreTests
    {
        [Test]
        public async Task SaveAsync_then_OpenReadAsync_round_trips_artifact_content()
        {
            var sut = new BlobMixLabArtifactStore(new FakeMixLabBlobGateway());
            byte[] payload = Encoding.UTF8.GetBytes("<html>report</html>");

            await sut.SaveAsync("r_1", "report.html", new MemoryStream(payload), CancellationToken.None);

            await using Stream read = await sut.OpenReadAsync("r_1", "report.html", CancellationToken.None);
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer, CancellationToken.None);

            buffer.ToArray().Should().Equal(payload);
        }
    }
}
