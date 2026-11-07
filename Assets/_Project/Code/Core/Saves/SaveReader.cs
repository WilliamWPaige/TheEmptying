using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheEmptying.Core.Entities;

namespace TheEmptying.Core.Saves
{
    /// <summary>Typed, always-little-endian binary reader matching SaveWriter.</summary>
    public sealed class SaveReader
    {
        private readonly Stream _stream;

        public SaveReader(Stream stream)
        {
            _stream = stream;
        }

        public int ReadInt() => BitConverter.ToInt32(ReadFixed(4), 0);
        public long ReadLong() => BitConverter.ToInt64(ReadFixed(8), 0);
        public ulong ReadULong() => unchecked((ulong)ReadLong());
        public double ReadDouble() => BitConverter.ToDouble(ReadFixed(8), 0);
        public bool ReadBool() => ReadByteOrThrow() != 0;

        public string ReadString()
        {
            int length = ReadInt();
            if (length < 0)
            {
                return null;
            }

            byte[] bytes = ReadRawBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public EntityId ReadEntityId()
        {
            int index = ReadInt();
            int generation = ReadInt();
            return new EntityId(index, generation);
        }

        public byte[] ReadRawBytes(int count)
        {
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = _stream.Read(buffer, read, count - read);
                if (n == 0)
                {
                    throw new EndOfStreamException("Unexpected end of save stream.");
                }

                read += n;
            }

            return buffer;
        }

        public byte[] ReadLengthPrefixedBytes()
        {
            int length = ReadInt();
            return ReadRawBytes(length);
        }

        public List<int> ReadIntList()
        {
            int count = ReadInt();
            var list = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadInt());
            }

            return list;
        }

        public List<long> ReadLongList()
        {
            int count = ReadInt();
            var list = new List<long>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadLong());
            }

            return list;
        }

        public List<double> ReadDoubleList()
        {
            int count = ReadInt();
            var list = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadDouble());
            }

            return list;
        }

        public List<bool> ReadBoolList()
        {
            int count = ReadInt();
            var list = new List<bool>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadBool());
            }

            return list;
        }

        public List<string> ReadStringList()
        {
            int count = ReadInt();
            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadString());
            }

            return list;
        }

        public List<EntityId> ReadEntityIdList()
        {
            int count = ReadInt();
            var list = new List<EntityId>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadEntityId());
            }

            return list;
        }

        private byte ReadByteOrThrow()
        {
            int b = _stream.ReadByte();
            if (b < 0)
            {
                throw new EndOfStreamException("Unexpected end of save stream.");
            }

            return (byte)b;
        }

        private byte[] ReadFixed(int count)
        {
            byte[] bytes = ReadRawBytes(count);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }
    }
}
