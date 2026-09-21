using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Ferrite.Core.Content;
using Ferrite.Core.Json;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Storage;

/// <summary>The on-disk shape, versioned so a later change can migrate rather than guess.</summary>
internal sealed class SavedProjectsDocument
{
    public int SchemaVersion { get; set; } = SavedProjectStore.CurrentSchemaVersion;

    public List<SavedProject> Projects { get; set; } = [];
}

/// <summary>
/// The user's saved provider projects. A corrupt document is preserved rather than mangled, and the
/// store starts empty instead of blocking the browser.
/// </summary>
public sealed class SavedProjectStore
{
    public const int CurrentSchemaVersion = 1;

    private readonly AppPaths _paths;
    private readonly ILogger<SavedProjectStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<SavedProject>? _projects;

    public SavedProjectStore(AppPaths paths, ILogger<SavedProjectStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Every saved project, newest first.</summary>
    public async Task<IReadOnlyList<SavedProject>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return [.. _projects!];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ContainsAsync(
        string provider,
        string projectId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var key = SavedProject.KeyFor(provider, projectId);
            return _projects!.Any(project => string.Equals(project.Key, key, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Adds the project if it is not saved, removes it if it is. Returns the new state.</summary>
    public async Task<bool> ToggleAsync(ContentSummary project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var key = SavedProject.KeyFor(project.Provider, project.ProjectId);
            var existing = _projects!.FirstOrDefault(saved =>
                string.Equals(saved.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _projects.Remove(existing);
                await PersistAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Removed saved project {Project}", project.Title);
                return false;
            }

            _projects.Insert(0, new SavedProject
            {
                Provider = project.Provider,
                ProjectId = project.ProjectId,
                Slug = project.Slug,
                Title = project.Title,
                ProjectType = project.ProjectType,
                IconUrl = project.IconUrl,
                Author = project.Author,
                SavedAt = DateTimeOffset.UtcNow,
            });
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Saved project {Project}", project.Title);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes a saved project by provider and id. Returns whether anything was removed.</summary>
    public async Task<bool> RemoveAsync(string provider, string projectId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var key = SavedProject.KeyFor(provider, projectId);
            var existing = _projects!.FirstOrDefault(saved =>
                string.Equals(saved.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return false;
            }

            _projects.Remove(existing);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    [MemberNotNull(nameof(_projects))]
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_projects is not null)
        {
            return;
        }

        // Default to empty before the first await: an unreadable document must leave the store usable
        // rather than half-loaded.
        _projects = [];
        var file = _paths.SavedProjectsFile;
        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            var bytes = await AtomicFile.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<SavedProjectsDocument>(bytes, JsonDefaults.Document);
            if (document?.Projects is { } loaded)
            {
                _projects = loaded;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Saved projects could not be read; preserving the file and starting empty");
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var document = new SavedProjectsDocument { Projects = _projects! };
        var json = JsonSerializer.Serialize(document, JsonDefaults.Document);
        await AtomicFile.WriteAllTextAsync(_paths.SavedProjectsFile, json, cancellationToken).ConfigureAwait(false);
    }
}
