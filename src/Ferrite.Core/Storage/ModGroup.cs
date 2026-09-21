namespace Ferrite.Core.Storage;

/// <summary>
/// A user-named group of mods. Membership is a rule rather than a stored list, so a group keeps
/// working when the pack updates: a mod belongs to the group when its display name or its file name
/// contains <see cref="Match"/>, case-insensitively.
/// </summary>
public sealed class ModGroup
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The substring a mod's name or file name has to contain to be in the group.</summary>
    public string Match { get; set; } = string.Empty;

    /// <summary>True when this group claims the given mod name and file name.</summary>
    public bool Matches(string? displayName, string? fileName) =>
        Match.Length > 0
        && (Contains(displayName) || Contains(fileName));

    private bool Contains(string? value) =>
        value is not null && value.Contains(Match, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Adds a group, or replaces the rule of an existing one with the same name. Returns false when
    /// the name or the rule is blank, in which case nothing is changed.
    /// </summary>
    public static bool Upsert(IList<ModGroup> groups, string? name, string? match)
    {
        var cleanName = name?.Trim();
        var cleanMatch = match?.Trim();
        if (string.IsNullOrEmpty(cleanName) || string.IsNullOrEmpty(cleanMatch))
        {
            return false;
        }

        var existing = groups.FirstOrDefault(group =>
            string.Equals(group.Name, cleanName, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null)
        {
            groups.Add(new ModGroup { Name = cleanName, Match = cleanMatch });
        }
        else
        {
            existing.Name = cleanName;
            existing.Match = cleanMatch;
        }

        return true;
    }

    /// <summary>Removes a group by name. Returns false when no group has that name.</summary>
    public static bool Remove(IList<ModGroup> groups, string? name)
    {
        var cleanName = name?.Trim();
        if (string.IsNullOrEmpty(cleanName))
        {
            return false;
        }

        var existing = groups.FirstOrDefault(group =>
            string.Equals(group.Name, cleanName, StringComparison.CurrentCultureIgnoreCase));
        return existing is not null && groups.Remove(existing);
    }
}
