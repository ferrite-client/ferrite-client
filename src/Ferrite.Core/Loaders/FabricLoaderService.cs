using System.Text.Json;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Net;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Loaders;

/// <summary>
/// Fabric and Quilt loaders. Both publish a version-document fragment per game/loader pair, which
/// merges with the vanilla document through the normal inheritance path.
/// </summary>
public sealed class FabricLoaderService
{
    public const string FabricMetaBase = "https://meta.fabricmc.net/v2";
    public const string QuiltMetaBase = "https://meta.quiltmc.org/v3";

    private const int MaxResponseBytes = 16 * 1024 * 1024;

    private readonly HttpService _http;
    private readonly VersionManifestService _manifest;
    private readonly ILogger<FabricLoaderService> _logger;

    public FabricLoaderService(
        HttpService http,
        VersionManifestService manifest,
        ILogger<FabricLoaderService> logger)
    {
        _http = http;
        _manifest = manifest;
        _logger = logger;
    }

    public Task<IReadOnlyList<LoaderVersionInfo>> ListFabricLoadersAsync(
        string minecraftVersion,
        CancellationToken cancellationToken) =>
        ListAsync(LoaderKind.Fabric, minecraftVersion, cancellationToken);

    public Task<IReadOnlyList<LoaderVersionInfo>> ListQuiltLoadersAsync(
        string minecraftVersion,
        CancellationToken cancellationToken) =>
        ListAsync(LoaderKind.Quilt, minecraftVersion, cancellationToken);

    public async Task<IReadOnlyList<LoaderVersionInfo>> ListAsync(
        LoaderKind kind,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);

        var url = kind switch
        {
            LoaderKind.Fabric => $"{FabricMetaBase}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}",
            LoaderKind.Quilt => $"{QuiltMetaBase}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}",
            _ => throw new LoaderException($"{kind} is not served by the Fabric meta API."),
        };

        var json = await _http.GetStringAsync(url, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        var results = new List<LoaderVersionInfo>();

        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var loaderElement = element.TryGetProperty("loader", out var loader) ? loader : element;
                var version = GetString(loaderElement, "version");
                if (string.IsNullOrEmpty(version))
                {
                    continue;
                }

                results.Add(new LoaderVersionInfo(
                    kind,
                    version,
                    minecraftVersion,
                    GetBool(loaderElement, "stable") ?? true,
                    GetString(loaderElement, "maven")));
            }
        }
        catch (JsonException exception)
        {
            throw new LoaderException($"{kind} loader metadata was not valid JSON.", exception);
        }

        _logger.LogInformation(
            "{Kind}: {Count} loader version(s) available for {Version}",
            kind,
            results.Count,
            minecraftVersion);
        return results;
    }

    /// <summary>
    /// Fetches the loader profile, stores it in the shared version store, and returns it. The
    /// stored document is what makes the loader instance launchable offline afterwards.
    /// </summary>
    public async Task<VersionDocument> InstallAsync(
        LoaderKind kind,
        string minecraftVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderVersion);

        var url = kind switch
        {
            LoaderKind.Fabric =>
                $"{FabricMetaBase}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json",
            LoaderKind.Quilt =>
                $"{QuiltMetaBase}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json",
            _ => throw new LoaderException($"{kind} is not served by the Fabric meta API."),
        };

        var bytes = await _http.GetBytesAsync(url, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        var profile = VersionManifestService.ParseVersionDocument(bytes, url);

        var expectedId = new LoaderVersionInfo(kind, loaderVersion, minecraftVersion, true, null).VersionId;
        profile.Id ??= expectedId;
        profile.InheritsFrom ??= minecraftVersion;

        if (!string.Equals(profile.InheritsFrom, minecraftVersion, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "{Kind} profile for {Loader} inherits from {Actual} instead of {Requested}",
                kind,
                loaderVersion,
                profile.InheritsFrom,
                minecraftVersion);
        }

        await _manifest.WriteLocalVersionAsync(profile.Id, profile, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Installed {Kind} {Loader} for {Version} as {Id}",
            kind,
            loaderVersion,
            minecraftVersion,
            profile.Id);
        return profile;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
