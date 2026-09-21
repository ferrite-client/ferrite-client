namespace Ferrite.Core.Game;

public enum NbtTagType : byte
{
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12,
}

/// <summary>
/// One NBT tag. Integral types are widened to <see cref="long"/>, floats to <see cref="double"/>,
/// compounds to a name-keyed map, and lists to an ordered collection. Read-only by design: Ferrite
/// only needs to inspect level data and server lists, so there is no writer to keep safe.
/// </summary>
public sealed class NbtTag
{
    public required NbtTagType Type { get; init; }

    public object? Value { get; init; }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, NbtTag>? Compound => Value as IReadOnlyDictionary<string, NbtTag>;

    public IReadOnlyList<NbtTag>? List => Value as IReadOnlyList<NbtTag>;

    public string? AsString() => Value as string;

    public long? AsLong() => Value switch
    {
        long value => value,
        short value => value,
        byte value => value,
        _ => null,
    };

    public int? AsInt() => AsLong() is { } value && value is >= int.MinValue and <= int.MaxValue
        ? (int)value
        : null;

    public double? AsDouble() => Value switch
    {
        double value => value,
        float value => value,
        long value => value,
        _ => null,
    };

    public bool? AsBool() => AsLong() is { } value ? value != 0 : null;

    /// <summary>Looks up a child by name in a compound tag.</summary>
    public NbtTag? this[string name] =>
        Compound is { } children && children.TryGetValue(name, out var child) ? child : null;

    /// <summary>Walks a path of compound keys, returning null when any step is missing.</summary>
    public NbtTag? Path(params string[] path)
    {
        NbtTag current = this;
        foreach (var segment in path)
        {
            if (current[segment] is not { } next)
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
