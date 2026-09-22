using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ferrite.App.ViewModels;

/// <summary>The tone a transient notification carries. It is never used to carry brand colour.</summary>
public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// One transient notification in the shell's toast area: the outcome of something the user asked
/// for, shown once and gone. It deliberately carries no navigation - if a result needs a decision,
/// it belongs in a banner or on the page, not in a toast.
/// </summary>
public sealed partial class ToastViewModel : ObservableObject
{
    public ToastViewModel(ToastKind kind, string title, string? detail, Action<ToastViewModel> dismiss)
    {
        Kind = kind;
        Title = title;
        Detail = detail;
        DismissCommand = new RelayCommand(() => dismiss(this));
    }

    public ToastKind Kind { get; }

    public string Title { get; }

    public string? Detail { get; }

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public IRelayCommand DismissCommand { get; }

    // Conditional style classes: the view binds these to Classes.success / .warning / .danger / .info so
    // the tone is carried by the design system rather than by a second set of templates here.
    public bool IsSuccess => Kind == ToastKind.Success;

    public bool IsWarning => Kind == ToastKind.Warning;

    public bool IsError => Kind == ToastKind.Error;

    public bool IsInfo => Kind == ToastKind.Info;
}
