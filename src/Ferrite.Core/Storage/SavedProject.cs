using Ferrite.Core.Content;

namespace Ferrite.Core.Storage;

/// <summary>
/// A provider project the user saved to revisit. It stores only what is needed to find the project
/// again, so nothing about it can go stale except the title.
/// </summary>
public sealed record SavedProject
{
    public required string Provider { get; init; }

    public required string ProjectId { get; init; }

    public string Slug { get; init; } = string.Empty;

    public required string Title { get; init; }

    public ContentProjectType ProjectType { get; init; }

    public string? IconUrl { get; init; }

    public string? Author { get; init; }

    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Provider plus project, which is what makes an entry unique.</summary>
    public static string KeyFor(string provider, string projectId) => $"{provider}:{projectId}";

    public string Key => KeyFor(Provider, ProjectId);
}
