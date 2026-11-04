using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>
    /// 讀取不可信 XML：禁止 DTD（防 XXE 與實體展開攻擊）、不解析外部資源、限制檔案大小。
    /// </summary>
    public static class SafeXml
    {
        public const long MaxFileBytes = 32L * 1024 * 1024;

        public static XDocument LoadFile(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                throw new InvalidDataException("XML 檔案過大：" + path);
            }
            using (var stream = File.OpenRead(path))
            {
                return Load(stream);
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

        public static XDocument Load(Stream stream)
        {
            using (var xml = XmlReader.Create(stream, Settings()))
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
                // 與遊戲使用的 XmlDocument（PreserveWhitespace = false）一致：純空白文字節點不保留。
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true,
                CloseInput = false,
            };
        }
    }
}
