using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Ferrite.App.Controls;

/// <summary>
/// A single stroked icon.
/// </summary>
/// <remarks>
/// Built as a templated control rather than a bare <see cref="Avalonia.Controls.Shapes.Path"/> so the
/// icon inherits <c>Foreground</c> from whatever it sits in. That keeps one icon definition usable on
/// a primary button, a toolbar, and a muted metadata row without repeating a brush at every call site.
/// </remarks>
public sealed class IconGlyph : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<IconGlyph, Geometry?>(nameof(Data));

    /// <summary>Stroke width in the icon's own 24-unit coordinate space.</summary>
    public static readonly StyledProperty<double> WeightProperty =
        AvaloniaProperty.Register<IconGlyph, double>(nameof(Weight), 1.7);

    /// <summary>Draw the geometry filled instead of stroked, for glyphs like Play and Stop.</summary>
    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<IconGlyph, bool>(nameof(IsFilled));

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Weight
    {
        get => GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }
}
