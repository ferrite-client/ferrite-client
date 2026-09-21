using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Ferrite.Core.Net;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Download;

/// <summary>
/// Bounded, resumable, hash-verifying download engine. Files are written to a temporary sibling,
/// verified, and only then moved into place, so an interrupted transfer can never be mistaken
/// for a valid artifact.
/// </summary>
public sealed class DownloadEngine
{
    private readonly HttpService _http;
    private readonly DownloadEngineOptions _options;
    private readonly ILogger<DownloadEngine> _logger;

    public DownloadEngine(HttpService http, DownloadEngineOptions options, ILogger<DownloadEngine> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<DownloadSummary> DownloadAsync(
        IReadOnlyList<DownloadRequest> requests,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return new DownloadSummary(0, 0, 0, []);
        }

        var unique = Deduplicate(requests);
        var tracker = new Tracker(progress, unique.Count, SumExpectedBytes(unique));

        try
        {
            var missing = new List<DownloadRequest>(unique.Count);
            foreach (var request in unique)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsSatisfiedAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    tracker.MarkSkipped(request.DisplayName);
                }
                else
                {
                    missing.Add(request);
                }
            }

            var failures = new List<DownloadFailure>();
            if (missing.Count > 0)
            {
                var failureGate = new object();
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrency),
                    CancellationToken = cancellationToken,
                };

                await Parallel.ForEachAsync(missing, parallelOptions, async (request, token) =>
                {
                    try
                    {
                        await DownloadOneAsync(request, tracker, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "Giving up on {Target}", request.TargetPath);
                        lock (failureGate)
                        {
                            failures.Add(new DownloadFailure(request.Url, request.TargetPath, exception.Message));
                        }

                        tracker.MarkFailed(request.DisplayName);
                    }
                }).ConfigureAwait(false);
            }

            var skipped = unique.Count - missing.Count;
            var downloaded = missing.Count - failures.Count;
            return new DownloadSummary(unique.Count, downloaded, skipped, failures);
        }
        finally
        {
            tracker.Complete();
        }
    }

    private static List<DownloadRequest> Deduplicate(IReadOnlyList<DownloadRequest> requests)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var result = new List<DownloadRequest>(requests.Count);
        foreach (var request in requests)
        {
            var key = Path.GetFullPath(request.TargetPath);
            if (seen.Add(key))
            {
                result.Add(request with { TargetPath = key });
            }
        }

        return result;
    }

    private static long SumExpectedBytes(IReadOnlyList<DownloadRequest> requests)
    {
        long total = 0;
        foreach (var request in requests)
        {
            if (request.ExpectedSize is { } size && size > 0)
            {
                total += size;
            }
        }

        return total;
    }

    private async Task<bool> IsSatisfiedAsync(DownloadRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.TargetPath))
        {
            return false;
        }

        var ok = await VerifyAsync(request.TargetPath, request, cancellationToken).ConfigureAwait(false);
        if (!ok)
        {
            _logger.LogInformation("Re-acquiring {Target}: existing file failed verification", request.TargetPath);
            AtomicFile.TryDelete(request.TargetPath);
        }

        return ok;
    }

    private async Task DownloadOneAsync(DownloadRequest request, Tracker tracker, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(request.TargetPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new DirectoryNotFoundException($"Target has no directory: {request.TargetPath}");
        }

        Directory.CreateDirectory(directory);
        var partPath = request.TargetPath + ".part";

        Exception? lastError = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.BeginItem(request.DisplayName, request.ExpectedSize ?? 0);

            try
            {
                await TransferAsync(request, partPath, tracker, cancellationToken).ConfigureAwait(false);

                if (!await VerifyAsync(partPath, request, cancellationToken).ConfigureAwait(false))
                {
                    throw new DownloadFailedException($"Checksum mismatch for {request.DisplayName}");
                }

                File.Move(partPath, request.TargetPath, overwrite: true);
                tracker.EndItem(success: true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AtomicFile.TryDelete(partPath);
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                AtomicFile.TryDelete(partPath);
                _logger.LogWarning(
                    "Attempt {Attempt}/{Max} failed for {Target}: {Message}",
                    attempt,
                    _options.MaxAttempts,
                    request.TargetPath,
                    exception.Message);

                if (attempt < _options.MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        tracker.EndItem(success: false);
        throw new DownloadFailedException($"Failed to download {request.DisplayName}", lastError);
    }

    private async Task TransferAsync(
        DownloadRequest request,
        string partPath,
        Tracker tracker,
        CancellationToken cancellationToken)
    {
        var urls = new List<string>(1 + request.FallbackUrls.Count) { request.Url };
        urls.AddRange(request.FallbackUrls);

        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                await TransferFromUrlAsync(url, partPath, request, tracker, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                _logger.LogInformation("Mirror {Url} failed: {Message}", url, exception.Message);
            }
        }

        throw new DownloadFailedException($"All mirrors failed for {request.DisplayName}", lastError);
    }

    private async Task TransferFromUrlAsync(
        string url,
        string partPath,
        DownloadRequest request,
        Tracker tracker,
        CancellationToken cancellationToken)
    {
        long resumeOffset = 0;
        if (_options.AllowResume && File.Exists(partPath))
        {
            resumeOffset = new FileInfo(partPath).Length;
            if (request.ExpectedSize is { } expected && resumeOffset >= expected)
            {
                // A full-length partial is more likely corrupt than resumable.
                AtomicFile.TryDelete(partPath);
                resumeOffset = 0;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PerAttemptTimeout);

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeOffset > 0)
        {
            message.Headers.Range = new RangeHeaderValue(resumeOffset, null);
        }

        using var response = await _http.SendStreamingWithRetryAsync(message, timeout.Token).ConfigureAwait(false);

        var appending = resumeOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!appending)
        {
            resumeOffset = 0;
        }
        else
        {
            tracker.AddBytes(resumeOffset);
        }

        await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using var file = new FileStream(
            partPath,
            appending ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            useAsync: true);

        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await content.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            tracker.AddBytes(read);
        }

        await file.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private static async Task<bool> VerifyAsync(
        string path,
        DownloadRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (request.ExpectedSize is { } size && new FileInfo(path).Length != size)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(request.ExpectedSha1))
        {
            var sha1 = await Hashing.HashFileAsync(path, HashAlgorithmName.SHA1, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sha1, request.ExpectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(request.ExpectedSha256))
        {
            var sha256 = await Hashing.HashFileAsync(path, HashAlgorithmName.SHA256, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sha256, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(request.ExpectedSha512))
        {
            var sha512 = await Hashing.HashFileAsync(path, HashAlgorithmName.SHA512, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sha512, request.ExpectedSha512, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Aggregates counters and throttles progress notifications to a readable rate.</summary>
    private sealed class Tracker
    {
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(180);

        private readonly IProgress<DownloadProgress>? _progress;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly object _gate = new();

        private int _totalFiles;
        private int _completedFiles;
        private int _failedFiles;
        private int _skippedFiles;
        private long _totalBytes;
        private long _completedBytes;
        private long _lastReportTimestamp;
        private string? _currentItem;

        public Tracker(IProgress<DownloadProgress>? progress, int totalFiles, long totalBytes)
        {
            _progress = progress;
            _totalFiles = totalFiles;
            _totalBytes = totalBytes;
        }

        public void BeginItem(string name, long expectedBytes)
        {
            lock (_gate)
            {
                _currentItem = name;
                if (expectedBytes > 0)
                {
                    _totalBytes = Math.Max(_totalBytes, expectedBytes);
                }
            }
        }

        public void AddBytes(long delta)
        {
            if (delta == 0)
            {
                return;
            }

            lock (_gate)
            {
                _completedBytes += delta;
            }

            ReportIfDue();
        }

        public void EndItem(bool success)
        {
            lock (_gate)
            {
                if (success)
                {
                    _completedFiles++;
                }
                else
                {
                    _failedFiles++;
                }
            }

            Report();
        }

        public void MarkSkipped(string name)
        {
            lock (_gate)
            {
                _skippedFiles++;
                _completedFiles++;
                _currentItem = name;
            }

            Report();
        }

        public void MarkFailed(string name)
        {
            lock (_gate)
            {
                _failedFiles++;
                _currentItem = name;
            }

            Report();
        }

        public void Complete() => Report();

        private void ReportIfDue()
        {
            var now = Stopwatch.GetTimestamp();
            var last = Interlocked.Read(ref _lastReportTimestamp);
            var intervalTicks = (long)(ReportInterval.TotalSeconds * Stopwatch.Frequency);
            if (now - last < intervalTicks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastReportTimestamp, now, last) == last)
            {
                Report();
            }
        }

        private void Report()
        {
            if (_progress is null)
            {
                return;
            }

            DownloadProgress snapshot;
            lock (_gate)
            {
                var elapsed = Math.Max(0.001, _stopwatch.Elapsed.TotalSeconds);
                snapshot = new DownloadProgress
                {
                    TotalFiles = _totalFiles,
                    CompletedFiles = _completedFiles,
                    FailedFiles = _failedFiles,
                    SkippedFiles = _skippedFiles,
                    TotalBytes = Math.Max(_totalBytes, _completedBytes),
                    CompletedBytes = _completedBytes,
                    BytesPerSecond = _completedBytes / elapsed,
                    CurrentItem = _currentItem,
                };
            }

            try
            {
                _progress.Report(snapshot);
            }
            catch (Exception)
            {
                // A progress callback must never fail a download.
            }
        }
    }
}
