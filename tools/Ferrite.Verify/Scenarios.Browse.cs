using Ferrite.Core.Content;

namespace Ferrite.Verify;

internal static partial class Scenarios
{
    /// <summary>
    /// The content browser against the live provider: the facets it offers, a filtered search, the
    /// project document behind a result, and the version and file a user would install.
    /// </summary>
    public static async Task<int> BrowseAsync(
        VerifyServices services,
        string query,
        string? category,
        string? gameVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        // Modrinth is the provider this scenario can exercise without a user-issued API key;
        // the CurseForge half of the same code path is tracked as an external blocker.
        IContentProvider provider = services.Modrinth;
        Console.WriteLine($"Provider: {provider.Name} (configured: {provider.IsConfigured})");
        if (!provider.IsConfigured)
        {
            Console.WriteLine($"Unavailable: {provider.UnavailableReason}");
            return 2;
        }

        foreach (var kind in new[] { "category", "loader", "game_version" })
        {
            var tags = await provider.GetTagsAsync(kind, cancellationToken).ConfigureAwait(false);
            var sample = tags.Take(6).Select(tag => tag.DisplayName ?? tag.Name);
            Console.WriteLine($"Facet {kind,-13} {tags.Count,5} value(s)  e.g. {string.Join(", ", sample)}");
        }

        var categories = category is { Length: > 0 } ? new[] { category } : null;
        Console.WriteLine();
        Console.WriteLine(
            $"Search: query='{query}' category={category ?? "-"} version={gameVersion ?? "-"} loader={loader ?? "-"}");
        var results = await provider
            .SearchAsync(
                new ContentSearchQuery(
                    query,
                    ContentProjectType.Mod,
                    gameVersion,
                    loader,
                    categories,
                    Limit: 5),
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"  total hits: {results.TotalHits}");
        foreach (var hit in results.Hits)
        {
            Console.WriteLine(
                $"  - {hit.Title} [{string.Join(',', hit.Categories)}] by {hit.Author} "
                + $"{hit.Downloads:N0} downloads");
        }

        if (results.Hits.Count == 0)
        {
            Console.WriteLine("No results to open.");
            return 3;
        }

        // Every result in a filtered search must actually carry the facet that was asked for.
        if (category is { Length: > 0 })
        {
            var missing = results.Hits
                .Where(hit => !hit.Categories.Any(value =>
                    string.Equals(value, category, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            Console.WriteLine(missing.Count == 0
                ? $"  every hit carries the '{category}' category"
                : $"  {missing.Count} hit(s) do not carry '{category}': {string.Join(", ", missing.Select(h => h.Title))}");
            if (missing.Count > 0)
            {
                return 4;
            }
        }

        var project = await provider
            .GetProjectAsync(results.Hits[0].ProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            Console.WriteLine("The provider returned no project document.");
            return 5;
        }

        Console.WriteLine();
        Console.WriteLine($"Project: {project.Title} ({project.Slug})");
        Console.WriteLine($"  licence:      {project.License ?? "-"}");
        Console.WriteLine($"  categories:   {string.Join(", ", project.Categories)}");
        Console.WriteLine($"  loaders:      {string.Join(", ", project.Loaders)}");
        Console.WriteLine($"  versions:     {project.GameVersions.Count} declared");
        Console.WriteLine($"  body:         {project.Body?.Length ?? 0} characters");
        Console.WriteLine($"  gallery:      {project.Gallery?.Count ?? 0} image(s)");
        Console.WriteLine($"  source:       {project.SourceUrl ?? "-"}");

        var versions = await provider
            .GetVersionsAsync(project.ProjectId, gameVersion, loader, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"  versions for the filters: {versions.Count}");
        if (versions.Count == 0)
        {
            return 6;
        }

        var newest = versions[0];
        Console.WriteLine(
            $"  newest: {newest.VersionNumber} ({newest.VersionType}), "
            + $"game {string.Join(',', newest.GameVersions.Take(3))}, loaders {string.Join(',', newest.Loaders)}");
        Console.WriteLine(
            $"  changelog: {(string.IsNullOrWhiteSpace(newest.Changelog) ? "none published" : newest.Changelog.Length + " characters")}");
        Console.WriteLine(
            $"  file: {newest.PrimaryFile?.FileName ?? "-"} "
            + $"({(newest.PrimaryFile is { } file ? file.Size : 0)} bytes, "
            + $"sha1 {(newest.PrimaryFile?.Sha1 is { Length: > 0 } ? "present" : "absent")})");

        var best = provider.SelectBestVersion(versions, gameVersion, loader);
        Console.WriteLine(best is null
            ? "  no version is compatible with the instance filters"
            : $"  best for the instance: {best.VersionNumber} ({best.VersionType})");

        return 0;
    }
}
