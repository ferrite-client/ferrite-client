namespace Ferrite.App.ViewModels;

/// <summary>
/// A selectable value together with the text shown for it. Used by the drop-downs that filter or
/// order a list, where the stored value and the visible label are not the same string.
/// </summary>
public sealed record ChoiceOption(string Value, string Label);
