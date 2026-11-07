using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Saves
{
    /// <summary>Typed, always-little-endian binary writer, so save files are identical across machines.</summary>
    public sealed class SaveWriter
    {
        private readonly Stream _stream;

        public SaveWriter(Stream stream)
        {
            _stream = stream;
        }

        public void WriteInt(int value) => WriteFixed(BitConverter.GetBytes(value));
        public void WriteLong(long value) => WriteFixed(BitConverter.GetBytes(value));
        public void WriteULong(ulong value) => WriteLong(unchecked((long)value));
        public void WriteDouble(double value) => WriteFixed(BitConverter.GetBytes(value));
        public void WriteBool(bool value) => _stream.WriteByte((byte)(value ? 1 : 0));

        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteInt(-1);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteEntityId(EntityId id)
        {
            WriteInt(id.Index);
            WriteInt(id.Generation);
        }

        public void WriteRawBytes(byte[] bytes)
        {
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteLengthPrefixedBytes(byte[] bytes)
        {
            WriteInt(bytes.Length);
            WriteRawBytes(bytes);
        }

        public void WriteIntList(IReadOnlyList<int> values)
        {
            WriteInt(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteInt(values[i]);
            }
        }

        public void WriteLongList(IReadOnlyList<long> values)
        {
            WriteInt(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteLong(values[i]);
            }
        }

        public void WriteDoubleList(IReadOnlyList<double> values)
        {
            WriteInt(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteDouble(values[i]);
            }
        }

        public void WriteBoolList(IReadOnlyList<bool> values)
        {
            WriteInt(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteBool(values[i]);
            }
        }

        public void WriteStringList(IReadOnlyList<string> values)
        {
            WriteInt(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteString(values[i]);
            }
        }

        public void WriteEntityIdList(IReadOnlyList<EntityId> values)
        {
            WriteInt(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                WriteEntityId(values[i]);
            }
        }

        private void WriteFixed(byte[] bytes)
        {
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            _stream.Write(bytes, 0, bytes.Length);
        }
    }
}
