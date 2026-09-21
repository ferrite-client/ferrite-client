using System.IO.Compression;
using System.Text;

namespace Ferrite.Core.Game;

/// <summary>
/// Writes NBT documents built from <see cref="NbtTag"/> values. The reader and writer share one
/// model, so there is no second representation to keep in sync. Used for <c>servers.dat</c>.
/// </summary>
public static class NbtWriter
{
    public static byte[] Write(NbtTag root, bool compress)
    {
        ArgumentNullException.ThrowIfNull(root);
        var payload = WritePayload(root);
        if (!compress)
        {
            return payload;
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(payload, 0, payload.Length);
        }

        return output.ToArray();
    }

    private static byte[] WritePayload(NbtTag root)
    {
        using var stream = new MemoryStream();
        // NBT is big-endian, so every multi-byte value is written byte by byte rather than through
        // BinaryWriter, which is little-endian.
        var writer = new BigEndianWriter(stream);
        writer.WriteByte((byte)root.Type);
        WriteString(writer, root.Name);
        WriteTag(writer, root);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteTag(BigEndianWriter writer, NbtTag tag)
    {
        switch (tag.Type)
        {
            case NbtTagType.Byte:
                writer.WriteByte((byte)(tag.AsLong() ?? 0));
                break;
            case NbtTagType.Short:
                writer.WriteInt16((short)(tag.AsLong() ?? 0));
                break;
            case NbtTagType.Int:
                writer.WriteInt32((int)(tag.AsLong() ?? 0));
                break;
            case NbtTagType.Long:
                writer.WriteInt64(tag.AsLong() ?? 0);
                break;
            case NbtTagType.Float:
                writer.WriteSingle((float)(tag.AsDouble() ?? 0));
                break;
            case NbtTagType.Double:
                writer.WriteDouble(tag.AsDouble() ?? 0);
                break;
            case NbtTagType.ByteArray:
                WriteArray(writer, tag.Value as byte[] ?? []);
                break;
            case NbtTagType.String:
                WriteString(writer, tag.AsString() ?? string.Empty);
                break;
            case NbtTagType.List:
                WriteList(writer, tag.List ?? []);
                break;
            case NbtTagType.Compound:
                WriteCompound(writer, tag.Compound);
                break;
            case NbtTagType.IntArray:
                WriteIntArray(writer, tag.Value as int[] ?? []);
                break;
            case NbtTagType.LongArray:
                WriteLongArray(writer, tag.Value as long[] ?? []);
                break;
            default:
                throw new NbtException($"Cannot write tag type {tag.Type}.");
        }
    }

    private static void WriteCompound(BigEndianWriter writer, IReadOnlyDictionary<string, NbtTag>? children)
    {
        if (children is not null)
        {
            foreach (var (name, child) in children)
            {
                writer.WriteByte((byte)child.Type);
                WriteString(writer, name);
                WriteTag(writer, child);
            }
        }

        writer.WriteByte((byte)NbtTagType.End);
    }

    private static void WriteList(BigEndianWriter writer, IReadOnlyList<NbtTag> items)
    {
        var elementType = items.Count > 0 ? items[0].Type : NbtTagType.End;
        writer.WriteByte((byte)elementType);
        writer.WriteInt32(items.Count);
        foreach (var item in items)
        {
            if (item.Type != elementType)
            {
                throw new NbtException("Every list element must share one tag type.");
            }

            WriteTag(writer, item);
        }
    }

    private static void WriteArray(BigEndianWriter writer, byte[] values)
    {
        writer.WriteInt32(values.Length);
        writer.WriteBytes(values);
    }

    private static void WriteIntArray(BigEndianWriter writer, int[] values)
    {
        writer.WriteInt32(values.Length);
        foreach (var value in values)
        {
            writer.WriteInt32(value);
        }
    }

    private static void WriteLongArray(BigEndianWriter writer, long[] values)
    {
        writer.WriteInt32(values.Length);
        foreach (var value in values)
        {
            writer.WriteInt64(value);
        }
    }

    private static void WriteString(BigEndianWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new NbtException("An NBT string cannot exceed 65535 bytes.");
        }

        writer.WriteUInt16((ushort)bytes.Length);
        writer.WriteBytes(bytes);
    }

    /// <summary>Big-endian writer, matching the NBT format's byte order.</summary>
    private sealed class BigEndianWriter
    {
        private readonly Stream _stream;

        public BigEndianWriter(Stream stream)
        {
            _stream = stream;
        }

        public void WriteByte(byte value) => _stream.WriteByte(value);

        public void WriteBytes(ReadOnlySpan<byte> value) => _stream.Write(value);

        public void WriteUInt16(ushort value)
        {
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)(value & 0xFF));
        }

        public void WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));

        public void WriteInt32(int value)
        {
            _stream.WriteByte((byte)(value >> 24));
            _stream.WriteByte((byte)(value >> 16));
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)value);
        }

        public void WriteInt64(long value)
        {
            for (var shift = 56; shift >= 0; shift -= 8)
            {
                _stream.WriteByte((byte)(value >> shift));
            }
        }

        public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

        public void WriteDouble(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

        public void Flush() => _stream.Flush();
    }
}
