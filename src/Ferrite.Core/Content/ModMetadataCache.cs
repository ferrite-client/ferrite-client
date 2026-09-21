using System.Collections.Concurrent;

namespace Ferrite.Core.Content;

/// <summary>
/// Remembers the metadata read out of a mod file, keyed by the file's size and write time. A large
/// pack is rescanned every time the tab is shown, and opening hundreds of jars again to read the same
/// descriptors is the most expensive thing the launcher does on that screen.
/// </summary>
/// <remarks>
/// The fingerprint is the pair the filesystem already tracks, so nothing is hashed to decide whether
/// the descriptor could have changed. A file whose size and timestamp are unchanged is treated as
/// unchanged; a file that was replaced with identical contents is re-read, which is the safe
/// direction to be wrong in.
/// </remarks>
public sealed class ModMetadataCache
{
    /// <summary>Enough entries for several large packs without growing without bound.</summary>
    public const int DefaultCapacity = 4096;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;
    private int _hits;
    private int _misses;

    public ModMetadataCache(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Reads since the last <see cref="ResetCounters"/> that were answered from the cache.</summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>Reads that had to open the file.</summary>
    public int Misses => Volatile.Read(ref _misses);

    public int Count => _entries.Count;

    public void ResetCounters()
    {
        Volatile.Write(ref _hits, 0);
        Volatile.Write(ref _misses, 0);
    }

    /// <summary>Returns the cached metadata when the file's fingerprint still matches.</summary>
    public bool TryGet(string path, long size, long modifiedTicks, out ModMetadata metadata)
    {
        if (_entries.TryGetValue(path, out var entry)
            && entry.Size == size
            && entry.ModifiedTicks == modifiedTicks)
        {
            metadata = entry.Metadata;
            Interlocked.Increment(ref _hits);
            return true;
        }

        metadata = null!;
        Interlocked.Increment(ref _misses);
        return false;
    }

    public void Set(string path, long size, long modifiedTicks, ModMetadata metadata)
    {
        if (_entries.Count >= _capacity)
        {
            // The scan is authoritative and short-lived, so dropping everything is a fair way to stay
            // bounded; a partial eviction policy would buy little here.
            _entries.Clear();
        }

        _entries[path] = new Entry(size, modifiedTicks, metadata);
    }

    public void Clear() => _entries.Clear();

    private readonly record struct Entry(long Size, long ModifiedTicks, ModMetadata Metadata);
}
