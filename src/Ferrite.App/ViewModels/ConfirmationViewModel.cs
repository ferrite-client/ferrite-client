using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ferrite.App.ViewModels;

/// <summary>
/// One blocking decision: a title, an explanation, and two answers.
/// </summary>
/// <remarks>
/// This is the only place Ferrite asks for confirmation, and it is used for decisions that genuinely
/// must stop the user - deleting an instance, deleting a world, signing an account out. Everything
/// else is an inline control, a menu, or a toast, because a dialog per action trains people to
/// dismiss dialogs.
/// </remarks>
public sealed partial class ConfirmationViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConfirmationViewModel(string title, string message, string confirmLabel, bool isDestructive)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        IsDestructive = isDestructive;
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmLabel { get; }

    /// <summary>True when the confirming answer destroys something, so it is painted as a warning.</summary>
    public bool IsDestructive { get; }

    /// <summary>Completes with the user's answer. Awaiting this is what makes the decision blocking.</summary>
    public Task<bool> Result => _completion.Task;

    [RelayCommand]
    private void Confirm() => _completion.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _completion.TrySetResult(false);

    /// <summary>Answers "no" without a decision, for when the dialog is superseded or the shell closes.</summary>
    internal void Dismiss() => _completion.TrySetResult(false);
}
