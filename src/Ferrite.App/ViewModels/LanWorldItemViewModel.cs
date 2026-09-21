using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Game;

namespace Ferrite.App.ViewModels;

/// <summary>One world currently announced on the local network.</summary>
public sealed partial class LanWorldItemViewModel : ObservableObject
{
    private readonly Func<LanWorldItemViewModel, Task> _add;

    public LanWorldItemViewModel(LanWorld world, Func<LanWorldItemViewModel, Task> add)
    {
        World = world;
        _add = add;
    }

    public LanWorld World { get; }

    public string Name => World.DisplayName;

    public string Address => World.Address;

    public string LastSeenText
    {
        get
        {
            var age = DateTimeOffset.UtcNow - World.LastSeen;
            return age.TotalSeconds < 5 ? "announced just now" : $"last announced {(int)age.TotalSeconds}s ago";
        }
    }

    [ObservableProperty]
    private string? _note;

    [RelayCommand]
    private Task AddToServersAsync() => _add(this);
}
