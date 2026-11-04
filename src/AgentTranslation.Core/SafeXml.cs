using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>
    /// Reads untrusted XML: DTDs prohibited (XXE and entity expansion), no external resources, size limit.
    /// </summary>
    public static class SafeXml
    {
        public const long MaxFileBytes = 32L * 1024 * 1024;

        public static XDocument LoadFile(string path) => LoadFile(path, keepComments: false);

        /// <summary>keepComments = true keeps comments (used to read &lt;!-- EN: ... --&gt; source notes in translation files).</summary>
        public static XDocument LoadFile(string path, bool keepComments)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                throw new InvalidDataException("XML file too large: " + path);
            }
            using (var stream = File.OpenRead(path))
            {
                return Load(stream, keepComments);
            }
        }

        public static XDocument Parse(string text)
        {
            using (var reader = new StringReader(text))
            using (var xml = XmlReader.Create(reader, Settings()))
            {
                return XDocument.Load(xml, LoadOptions.SetLineInfo);
            }
        }

        public static XDocument Load(Stream stream) => Load(stream, keepComments: false);

        public static XDocument Load(Stream stream, bool keepComments)
        {
            var settings = Settings();
            settings.IgnoreComments = !keepComments;
            using (var xml = XmlReader.Create(stream, settings))
            {
                return XDocument.Load(xml, LoadOptions.SetLineInfo);
            }
        }

        private static XmlReaderSettings Settings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = MaxFileBytes,
                IgnoreComments = true,
                // Same as the game's XmlDocument (PreserveWhitespace = false): whitespace-only text nodes are dropped.
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true,
                CloseInput = false,
            };
        }
    }
}
