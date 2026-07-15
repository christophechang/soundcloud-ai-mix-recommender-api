using System.IO;
using System.IO.Compression;
using System.Text;
using Changsta.Ai.Core.BusinessProcesses.MixLab;
using FluentAssertions;
using NUnit.Framework;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    [TestFixture]
    public sealed class RekordboxPlaylistReaderTests
    {
        private const string Xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <DJ_PLAYLISTS Version="1.0.0">
              <COLLECTION Entries="0"></COLLECTION>
              <PLAYLISTS>
                <NODE Type="0" Name="ROOT" Count="2">
                  <NODE Name="DJ CRATES 2025" Type="0" Count="1">
                    <NODE Name="Eclectic" Type="1" KeyType="0" Entries="1">
                      <TRACK Key="101"/>
                    </NODE>
                  </NODE>
                  <NODE Name="Top Level Mix" Type="1" KeyType="0" Entries="1">
                    <TRACK Key="102"/>
                  </NODE>
                </NODE>
              </PLAYLISTS>
            </DJ_PLAYLISTS>
            """;

        [Test]
        public void ReadPlaylistPaths_strips_ROOT_and_returns_full_folder_paths()
        {
            var paths = RekordboxPlaylistReader.ReadPlaylistPaths(Gzip(Xml));

            paths.Should().Equal("DJ CRATES 2025/Eclectic", "Top Level Mix");
        }

        [Test]
        public void ReadPlaylistPaths_throws_on_non_xml_content()
        {
            var read = () => RekordboxPlaylistReader.ReadPlaylistPaths(Gzip("this is not xml <<<"));

            read.Should().Throw<System.Xml.XmlException>();
        }

        [Test]
        public void ReadPlaylistPaths_throws_on_non_gzip_stream()
        {
            var read = () => RekordboxPlaylistReader.ReadPlaylistPaths(new MemoryStream(new byte[] { 1, 2, 3, 4 }));

            read.Should().Throw<InvalidDataException>();
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
    }
}
