using Ferrite.Core.Game;

namespace Ferrite.Core.Tests;

public sealed class NbtTests
{
    [Fact]
    public void Round_trips_every_tag_type()
    {
        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = "root",
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["byte"] = new NbtTag { Type = NbtTagType.Byte, Value = 7L },
                ["short"] = new NbtTag { Type = NbtTagType.Short, Value = -300L },
                ["int"] = new NbtTag { Type = NbtTagType.Int, Value = 123456L },
                ["long"] = new NbtTag { Type = NbtTagType.Long, Value = 9_000_000_000L },
                ["float"] = new NbtTag { Type = NbtTagType.Float, Value = 1.5d },
                ["double"] = new NbtTag { Type = NbtTagType.Double, Value = 2.25d },
                ["string"] = new NbtTag { Type = NbtTagType.String, Value = "hello" },
                ["bytes"] = new NbtTag { Type = NbtTagType.ByteArray, Value = new byte[] { 1, 2, 3 } },
                ["ints"] = new NbtTag { Type = NbtTagType.IntArray, Value = new[] { 4, 5 } },
                ["longs"] = new NbtTag { Type = NbtTagType.LongArray, Value = new[] { 6L, 7L } },
                ["list"] = new NbtTag
                {
                    Type = NbtTagType.List,
                    Value = new List<NbtTag>
                    {
                        new() { Type = NbtTagType.String, Value = "a" },
                        new() { Type = NbtTagType.String, Value = "b" },
                    },
                },
                ["nested"] = new NbtTag
                {
                    Type = NbtTagType.Compound,
                    Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                    {
                        ["inner"] = new NbtTag { Type = NbtTagType.Int, Value = 42L },
                    },
                },
            },
        };

        foreach (var compress in new[] { false, true })
        {
            var parsed = NbtReader.Read(NbtWriter.Write(root, compress));

            Assert.Equal("root", parsed.Name);
            Assert.Equal(7L, parsed["byte"]!.AsLong());
            Assert.Equal(-300L, parsed["short"]!.AsLong());
            Assert.Equal(123456L, parsed["int"]!.AsLong());
            Assert.Equal(9_000_000_000L, parsed["long"]!.AsLong());
            Assert.Equal(1.5d, parsed["float"]!.AsDouble());
            Assert.Equal(2.25d, parsed["double"]!.AsDouble());
            Assert.Equal("hello", parsed["string"]!.AsString());
            Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])parsed["bytes"]!.Value!);
            Assert.Equal(new[] { 4, 5 }, (int[])parsed["ints"]!.Value!);
            Assert.Equal(new[] { 6L, 7L }, (long[])parsed["longs"]!.Value!);
            Assert.Equal(2, parsed["list"]!.List!.Count);
            Assert.Equal(42L, parsed.Path("nested", "inner")!.AsLong());
        }
    }

    [Fact]
    public void Truncated_documents_are_rejected()
    {
        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = "root",
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["value"] = new NbtTag { Type = NbtTagType.String, Value = new string('x', 64) },
            },
        };

        var bytes = NbtWriter.Write(root, compress: false);
        Assert.Throws<NbtException>(() => NbtReader.Read(bytes[..(bytes.Length / 2)]));
    }

    [Fact]
    public void Absurd_list_lengths_are_rejected_before_allocation()
    {
        var payload = new List<byte> { (byte)NbtTagType.Compound, 0x00, 0x04 };
        payload.AddRange("root"u8.ToArray());
        payload.Add((byte)NbtTagType.List);
        payload.Add(0x00);
        payload.AddRange("list"u8.ToArray());
        payload.Add((byte)NbtTagType.Int);
        payload.AddRange(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });

        Assert.Throws<NbtException>(() => NbtReader.Read(payload.ToArray()));
    }

    [Fact]
    public void Deeply_nested_documents_are_rejected()
    {
        NbtTag current = new() { Type = NbtTagType.Int, Value = 1L };
        for (var depth = 0; depth < 80; depth++)
        {
            current = new NbtTag
            {
                Type = NbtTagType.Compound,
                Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal) { ["child"] = current },
            };
        }

        Assert.Throws<NbtException>(() => NbtReader.Read(NbtWriter.Write(current, compress: false)));
    }

    [Fact]
   public void Empty_documents_are_rejected()
   {
       Assert.Throws<NbtException>(() => NbtReader.Read([(byte)NbtTagType.End]));
   }

    [Fact]
    public void Round_trips_a_list_of_compounds()
    {
        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = string.Empty,
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["servers"] = new NbtTag
                {
                    Type = NbtTagType.List,
                    Value = new List<NbtTag>
                    {
                        new()
                        {
                            Type = NbtTagType.Compound,
                            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                            {
                                ["name"] = new NbtTag { Type = NbtTagType.String, Name = "name", Value = "Alpha" },
                                ["ip"] = new NbtTag { Type = NbtTagType.String, Name = "ip", Value = "alpha.example" },
                            },
                        },
                        new()
                        {
                            Type = NbtTagType.Compound,
                            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
                            {
                                ["name"] = new NbtTag { Type = NbtTagType.String, Name = "name", Value = "Beta" },
                                ["ip"] = new NbtTag { Type = NbtTagType.String, Name = "ip", Value = "beta.example" },
                            },
                        },
                    },
                },
            },
        };

        var parsed = NbtReader.Read(NbtWriter.Write(root, compress: false));
        var servers = parsed["servers"]!.List!;

        Assert.Equal(2, servers.Count);
        Assert.Equal("Alpha", servers[0]["name"]!.AsString());
        Assert.Equal("beta.example", servers[1]["ip"]!.AsString());
    }

    [Fact]
    public void Gzip_documents_are_detected_by_magic_bytes()
    {
        var root = new NbtTag
        {
            Type = NbtTagType.Compound,
            Name = string.Empty,
            Value = new Dictionary<string, NbtTag>(StringComparer.Ordinal)
            {
                ["LevelName"] = new NbtTag { Type = NbtTagType.String, Value = "Compressed" },
            },
        };

        var bytes = NbtWriter.Write(root, compress: true);
        Assert.Equal(0x1F, bytes[0]);
        Assert.Equal("Compressed", NbtReader.Read(bytes)["LevelName"]!.AsString());
    }
}
