using System.Windows.Input;

namespace Ferrite.App.ViewModels;

/// <summary>
/// One entry in the quick-action palette. The command runs the action and closes the palette, so a
/// result carries its own behaviour rather than the view having to work out what was chosen.
/// </summary>
public sealed record QuickActionItem(string Title, string? Detail, ICommand Command)
{
    /// <summary>True when there is a second line to show, so the view can collapse an empty one.</summary>
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
