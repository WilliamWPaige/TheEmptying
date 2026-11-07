using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace TheEmptying.Core.Saves
{
    /// <summary>
    /// The binary envelope: identifying bytes and a format version; a small
    /// readable JSON header; a table of sections (Id, version, length); then
    /// the section data, each section gzip-compressed on its own. The header
    /// can be read without touching the section table or data.
    /// </summary>
    public static class SaveFile
    {
        private static readonly byte[] Magic = { (byte)'E', (byte)'M', (byte)'P', (byte)'T', (byte)'Y', (byte)'S', (byte)'A', (byte)'V' };

        public const int FormatVersion = 1;

        public static void Write(Stream stream, SaveHeader header, IReadOnlyList<SaveSectionData> sections)
        {
            var writer = new SaveWriter(stream);

            writer.WriteRawBytes(Magic);
            writer.WriteInt(FormatVersion);

            string headerJson = JsonConvert.SerializeObject(header);
            writer.WriteLengthPrefixedBytes(Encoding.UTF8.GetBytes(headerJson));

            var compressed = new List<(string Id, int Version, byte[] Bytes)>(sections.Count);
            foreach (SaveSectionData section in sections)
            {
                compressed.Add((section.Id, section.Version, Compress(section.Data)));
            }

            writer.WriteInt(compressed.Count);
            foreach (var section in compressed)
            {
                writer.WriteString(section.Id);
                writer.WriteInt(section.Version);
                writer.WriteInt(section.Bytes.Length);
            }

            foreach (var section in compressed)
            {
                writer.WriteRawBytes(section.Bytes);
            }
        }

        /// <summary>Reads only the header. Stops before the section table, so it never touches or decompresses the body.</summary>
        public static SaveHeader ReadHeader(Stream stream)
        {
            var reader = new SaveReader(stream);
            ReadAndCheckPreamble(reader);
            byte[] headerBytes = reader.ReadLengthPrefixedBytes();
            return JsonConvert.DeserializeObject<SaveHeader>(Encoding.UTF8.GetString(headerBytes));
        }

        public static (SaveHeader Header, Dictionary<string, SaveSectionData> Sections) ReadAll(Stream stream)
        {
            var reader = new SaveReader(stream);
            ReadAndCheckPreamble(reader);

            byte[] headerBytes = reader.ReadLengthPrefixedBytes();
            SaveHeader header = JsonConvert.DeserializeObject<SaveHeader>(Encoding.UTF8.GetString(headerBytes));

            int sectionCount = reader.ReadInt();
            var table = new List<(string Id, int Version, int Length)>(sectionCount);
            for (int i = 0; i < sectionCount; i++)
            {
                string id = reader.ReadString();
                int version = reader.ReadInt();
                int length = reader.ReadInt();
                table.Add((id, version, length));
            }

            var sections = new Dictionary<string, SaveSectionData>();
            foreach (var entry in table)
            {
                byte[] compressed = reader.ReadRawBytes(entry.Length);
                sections[entry.Id] = new SaveSectionData(entry.Id, entry.Version, Decompress(compressed));
            }

            return (header, sections);
        }

        private static void ReadAndCheckPreamble(SaveReader reader)
        {
            byte[] magic = reader.ReadRawBytes(Magic.Length);
            if (!BytesEqual(magic, Magic))
            {
                throw new InvalidDataException("Not a valid save file.");
            }

            int formatVersion = reader.ReadInt();
            if (formatVersion != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported save format version {formatVersion}.");
            }
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(data, 0, data.Length);
            }

            return output.ToArray();
        }

        private static byte[] Decompress(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
