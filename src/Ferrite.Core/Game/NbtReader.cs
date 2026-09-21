using System.IO.Compression;
using System.Text;

namespace Ferrite.Core.Game;

/// <summary>
/// Reads NBT documents (gzip-compressed or raw). Every length and depth is bounded before it is
/// used, because level data and server lists come from files the launcher did not write.
/// </summary>
public static class NbtReader
{
    private const int MaxDepth = 64;
    private const int MaxCollectionLength = 4 * 1024 * 1024;
    private const int MaxArrayBytes = 64 * 1024 * 1024;

    /// <summary>Reads an NBT file, transparently decompressing it when it is gzip-compressed.</summary>
    public static NbtTag ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Read(bytes);
    }

    public static NbtTag Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var compressed = new MemoryStream(bytes);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);
            return ReadPayload(decompressed.ToArray());
        }

        return ReadPayload(bytes);
    }

    private static NbtTag ReadPayload(byte[] bytes)
    {
        var reader = new SpanReader(bytes);
        var type = (NbtTagType)reader.ReadByte();
        if (type == NbtTagType.End)
        {
            throw new NbtException("The document contains no root tag.");
        }

        var name = reader.ReadString();
        var root = ReadTag(ref reader, type, depth: 0);
        root.Name = name;
        return root;
    }

    private static NbtTag ReadTag(ref SpanReader reader, NbtTagType type, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new NbtException($"NBT nesting exceeds the {MaxDepth} level limit.");
        }

        return type switch
        {
            NbtTagType.Byte => new NbtTag { Type = type, Value = (long)reader.ReadByte() },
            NbtTagType.Short => new NbtTag { Type = type, Value = (long)reader.ReadInt16() },
            NbtTagType.Int => new NbtTag { Type = type, Value = (long)reader.ReadInt32() },
            NbtTagType.Long => new NbtTag { Type = type, Value = reader.ReadInt64() },
            NbtTagType.Float => new NbtTag { Type = type, Value = (double)reader.ReadSingle() },
            NbtTagType.Double => new NbtTag { Type = type, Value = reader.ReadDouble() },
            NbtTagType.ByteArray => ReadByteArray(ref reader),
            NbtTagType.String => new NbtTag { Type = type, Value = reader.ReadString() },
            NbtTagType.List => ReadList(ref reader, depth),
            NbtTagType.Compound => ReadCompound(ref reader, depth),
            NbtTagType.IntArray => ReadIntArray(ref reader),
            NbtTagType.LongArray => ReadLongArray(ref reader),
            _ => throw new NbtException($"Unsupported tag type {(byte)type}."),
        };
    }

    private static NbtTag ReadCompound(ref SpanReader reader, int depth)
    {
        var children = new Dictionary<string, NbtTag>(StringComparer.Ordinal);
        while (true)
        {
            var type = (NbtTagType)reader.ReadByte();
            if (type == NbtTagType.End)
            {
                break;
            }

            var name = reader.ReadString();
            var child = ReadTag(ref reader, type, depth + 1);
            child.Name = name;
            children[name] = child;
        }

        return new NbtTag { Type = NbtTagType.Compound, Value = children };
    }

    private static NbtTag ReadList(ref SpanReader reader, int depth)
    {
        var elementType = (NbtTagType)reader.ReadByte();
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxCollectionLength)
        {
            throw new NbtException($"List length {length} is out of range.");
        }

        var items = new List<NbtTag>(Math.Min(length, 1024));
        for (var index = 0; index < length; index++)
        {
            items.Add(elementType == NbtTagType.End
                ? new NbtTag { Type = NbtTagType.End }
                : ReadTag(ref reader, elementType, depth + 1));
        }

        return new NbtTag { Type = NbtTagType.List, Value = items };
    }

    private static NbtTag ReadByteArray(ref SpanReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxArrayBytes)
        {
            throw new NbtException($"Byte array length {length} is out of range.");
        }

        return new NbtTag { Type = NbtTagType.ByteArray, Value = reader.ReadBytes(length) };
    }

    private static NbtTag ReadIntArray(ref SpanReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxArrayBytes / 4)
        {
            throw new NbtException($"Int array length {length} is out of range.");
        }

        var values = new int[length];
        for (var index = 0; index < length; index++)
        {
            values[index] = reader.ReadInt32();
        }

        return new NbtTag { Type = NbtTagType.IntArray, Value = values };
    }

    private static NbtTag ReadLongArray(ref SpanReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxArrayBytes / 8)
        {
            throw new NbtException($"Long array length {length} is out of range.");
        }

        var values = new long[length];
        for (var index = 0; index < length; index++)
        {
            values[index] = reader.ReadInt64();
        }

        return new NbtTag { Type = NbtTagType.LongArray, Value = values };
    }

    /// <summary>Big-endian reader with bounds checks on every read.</summary>
    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public SpanReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return _data[_position++];
        }

        public short ReadInt16()
        {
            EnsureAvailable(2);
            var value = (short)((_data[_position] << 8) | _data[_position + 1]);
            _position += 2;
            return value;
        }

        public int ReadInt32()
        {
            EnsureAvailable(4);
            var value = (_data[_position] << 24)
                | (_data[_position + 1] << 16)
                | (_data[_position + 2] << 8)
                | _data[_position + 3];
            _position += 4;
            return value;
        }

        public long ReadInt64()
        {
            EnsureAvailable(8);
            long value = 0;
            for (var index = 0; index < 8; index++)
            {
                value = (value << 8) | _data[_position + index];
            }

            _position += 8;
            return value;
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        public string ReadString()
        {
            EnsureAvailable(2);
            // NBT string lengths are unsigned 16-bit, so 64 KiB is the format's own ceiling.
            var length = (ushort)((_data[_position] << 8) | _data[_position + 1]);
            _position += 2;
            EnsureAvailable(length);
            var value = Encoding.UTF8.GetString(_data.Slice(_position, length));
            _position += length;
            return value;
        }

        public byte[] ReadBytes(int length)
        {
            EnsureAvailable(length);
            var value = _data.Slice(_position, length).ToArray();
            _position += length;
            return value;
        }

        private void EnsureAvailable(int count)
        {
            if (count < 0 || _position + count > _data.Length)
            {
                throw new NbtException(
                    $"The document ended after {_position} byte(s) while {count} more were needed.");
            }
        }
    }
}
