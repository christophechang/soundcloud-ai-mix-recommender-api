using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Parses playlist paths from an uploaded (gzipped) Rekordbox collection XML. Mirrors the
    /// Python CLI's parse_playlists (MixLab/src/mixlab/reader.py): the outer ROOT wrapper's name
    /// is stripped, Type="0" nodes are folders (contributing a "Name/" prefix), and Type="1" nodes
    /// are playlists whose full "folder/name" path is returned.
    /// </summary>
    public static class RekordboxPlaylistReader
    {
        public static IReadOnlyList<string> ReadPlaylistPaths(Stream gzipStream)
        {
            using var gzip = new GZipStream(gzipStream, CompressionMode.Decompress);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(gzip, settings);
            XDocument document = XDocument.Load(reader);

            var paths = new List<string>();
            XElement? playlists = document.Descendants("PLAYLISTS").FirstOrDefault();
            if (playlists is null)
            {
                return paths;
            }

            // Top-level children are walked with the outer ROOT node's name excluded.
            foreach (XElement child in playlists.Elements("NODE"))
            {
                Walk(child, prefix: string.Empty, includeName: false, paths);
            }

            return paths;
        }

        private static void Walk(XElement node, string prefix, bool includeName, List<string> paths)
        {
            string type = (string?)node.Attribute("Type") ?? string.Empty;
            string name = (string?)node.Attribute("Name") ?? string.Empty;

            if (type == "0")
            {
                string nextPrefix = prefix;
                if (includeName && name.Length > 0)
                {
                    nextPrefix = $"{prefix}{name}/";
                }

                foreach (XElement child in node.Elements("NODE"))
                {
                    Walk(child, nextPrefix, includeName: true, paths);
                }

                return;
            }

            if (type == "1" && name.Length > 0)
            {
                paths.Add($"{prefix}{name}");
            }
        }
    }
}
