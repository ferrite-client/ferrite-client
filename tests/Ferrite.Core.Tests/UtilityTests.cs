using Ferrite.Core.Util;

namespace Ferrite.Core.Tests;

public sealed class UtilityTests : IDisposable
{
    private readonly string _workspace;

    public UtilityTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-util-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Sha1_matches_known_vector()
    {
        Assert.Equal("aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d", Hashing.Sha1("hello"u8));
    }

    [Fact]
    public void Sha256_and_sha512_match_known_vectors()
    {
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            Hashing.Sha256("hello"u8));
        Assert.Equal(
            "9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca72323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043",
            Hashing.Sha512("hello"u8));
    }

    [Fact]
    public async Task HashFile_agrees_with_in_memory_hash()
    {
        var path = Path.Combine(_workspace, "sample.bin");
        var payload = new byte[200_000];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(path, payload);

        Assert.Equal(Hashing.Sha1(payload), await Hashing.HashFileSha1Async(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AtomicFile_write_replaces_and_leaves_no_temporary()
    {
        var path = Path.Combine(_workspace, "nested", "settings.json");
        await AtomicFile.WriteAllTextAsync(path, "{\"a\":1}", TestContext.Current.CancellationToken);
        await AtomicFile.WriteAllTextAsync(path, "{\"a\":2}", TestContext.Current.CancellationToken);

        Assert.Equal("{\"a\":2}", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
    }

    [Fact]
    public async Task Verify_rejects_size_or_hash_mismatch()
    {
        var path = Path.Combine(_workspace, "verify.bin");
        await File.WriteAllBytesAsync(path, "hello"u8.ToArray());

        Assert.True(await Hashing.VerifyAsync(path, 5, Hashing.Sha1("hello"u8), TestContext.Current.CancellationToken));
        Assert.False(await Hashing.VerifyAsync(path, 6, Hashing.Sha1("hello"u8), TestContext.Current.CancellationToken));
        Assert.False(await Hashing.VerifyAsync(path, 5, Hashing.Sha1("other"u8), TestContext.Current.CancellationToken));
        Assert.False(await Hashing.VerifyAsync(path, null, Hashing.Sha1("other"u8), TestContext.Current.CancellationToken));
        Assert.True(await Hashing.VerifyAsync(path, null, null, TestContext.Current.CancellationToken));
        Assert.False(await Hashing.VerifyAsync(path + ".missing", null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ByteSize_formats_readably()
    {
        Assert.Equal("0 B", ByteSize.Format(0));
        Assert.Equal("512 B", ByteSize.Format(512));
        Assert.Equal("1 KiB", ByteSize.Format(1024));
        Assert.Equal("1.5 MiB", ByteSize.Format(1024 * 1024 + 512 * 1024));
    }

    [Fact]
    public void ByteSize_formats_negative_as_zero()
    {
        Assert.Equal("0 B", ByteSize.Format(-1));
    }
}
