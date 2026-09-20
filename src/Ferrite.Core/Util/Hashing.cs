using System.Security.Cryptography;

namespace Ferrite.Core.Util;

public static class Hashing
{
    private const int BufferSize = 128 * 1024;

    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Sha1(ReadOnlySpan<byte> bytes) =>
        ToHex(SHA1.HashData(bytes));

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        ToHex(SHA256.HashData(bytes));

    public static string Sha512(ReadOnlySpan<byte> bytes) =>
        ToHex(SHA512.HashData(bytes));

    public static async Task<string> HashFileAsync(
        string path,
        HashAlgorithmName algorithm,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);

        using var incremental = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            incremental.AppendData(buffer, 0, read);
        }

        return ToHex(incremental.GetHashAndReset());
    }

    public static Task<string> HashFileSha1Async(string path, CancellationToken cancellationToken) =>
        HashFileAsync(path, HashAlgorithmName.SHA1, cancellationToken);

    public static Task<string> HashFileSha512Async(string path, CancellationToken cancellationToken) =>
        HashFileAsync(path, HashAlgorithmName.SHA512, cancellationToken);

    /// <summary>
    /// Verifies an existing file against an optional size and SHA-1 hash. A file whose size does
    /// not match is never hashed, which keeps verification cheap for large libraries.
    /// </summary>
    public static async Task<bool> VerifyAsync(
        string path,
        long? expectedSize,
        string? expectedSha1,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (expectedSize is { } size)
        {
            var info = new FileInfo(path);
            if (info.Length != size)
            {
                return false;
            }
        }

        if (string.IsNullOrEmpty(expectedSha1))
        {
            return true;
        }

        var actual = await HashFileSha1Async(path, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}
