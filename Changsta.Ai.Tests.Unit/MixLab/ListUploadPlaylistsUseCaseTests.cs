using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using Changsta.Ai.Core.Contracts.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class ListUploadPlaylistsUseCaseTests
    {
        private const string Xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <DJ_PLAYLISTS Version="1.0.0">
              <PLAYLISTS>
                <NODE Type="0" Name="ROOT" Count="1">
                  <NODE Name="Crates" Type="0" Count="1">
                    <NODE Name="Eclectic" Type="1" Entries="1"><TRACK Key="1"/></NODE>
                  </NODE>
                </NODE>
              </PLAYLISTS>
            </DJ_PLAYLISTS>
            """;

        [Test]
        public async Task ListAsync_returns_found_with_stripped_paths()
        {
            var open = new StubOpenUploadUseCase(new MixLabUploadContent("u_1", Gzip(Xml)));
            var sut = new ListUploadPlaylistsUseCase(open);

            ListUploadPlaylistsResult result = await sut.ListAsync("latest", CancellationToken.None);

            result.Outcome.Should().Be(ListUploadPlaylistsResult.ListOutcome.Found);
            result.Playlists.Should().Equal("Crates/Eclectic");
        }

        [Test]
        public async Task ListAsync_returns_upload_not_found_when_open_returns_null()
        {
            var sut = new ListUploadPlaylistsUseCase(new StubOpenUploadUseCase(null));

            ListUploadPlaylistsResult result = await sut.ListAsync("missing", CancellationToken.None);

            result.Outcome.Should().Be(ListUploadPlaylistsResult.ListOutcome.UploadNotFound);
        }

        [Test]
        public async Task ListAsync_returns_parse_failed_on_garbage_content()
        {
            var open = new StubOpenUploadUseCase(new MixLabUploadContent("u_1", Gzip("not xml <<<")));
            var sut = new ListUploadPlaylistsUseCase(open);

            ListUploadPlaylistsResult result = await sut.ListAsync("u_1", CancellationToken.None);

            result.Outcome.Should().Be(ListUploadPlaylistsResult.ListOutcome.ParseFailed);
        }

        private static Stream Gzip(string content)
        {
            var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }

            output.Position = 0;
            return output;
        }

        private sealed class StubOpenUploadUseCase : IOpenUploadUseCase
        {
            private readonly MixLabUploadContent? _content;

            public StubOpenUploadUseCase(MixLabUploadContent? content)
            {
                _content = content;
            }

            public Task<MixLabUploadContent?> OpenAsync(string uploadId, CancellationToken cancellationToken) =>
                Task.FromResult(_content);
        }
    }
}
