using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Game;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>One saved world with its actions.</summary>
public sealed partial class WorldItemViewModel : ObservableObject
{
    private readonly Func<WorldItemViewModel, Task> _backup;
    private readonly Func<WorldItemViewModel, Task> _delete;
    private readonly Func<WorldItemViewModel, Task> _duplicate;

    public WorldItemViewModel(
        WorldInfo world,
        Func<WorldItemViewModel, Task> backup,
        Func<WorldItemViewModel, Task> delete,
        Func<WorldItemViewModel, Task> duplicate)
    {
        World = world;
        _backup = backup;
        _delete = delete;
        _duplicate = duplicate;
    }

    public WorldInfo World { get; }

    public string Name => World.Name;

    public string DetailText =>
        $"{World.GameMode}{(World.Hardcore ? " · hardcore" : string.Empty)} · "
        + $"v{World.VersionName ?? "?"} · {World.SizeText} · last played {World.LastPlayedText}";

    public bool HasIcon => World.HasIcon;

    public string? IconPath => World.IconPath;

    public string SeedText => World.Seed is { } seed ? $"seed {seed}" : "seed unknown";

    [ObservableProperty]
    private string? _note;

    [RelayCommand]
    private Task Backup() => _backup(this);

    [RelayCommand]
    private Task Delete() => _delete(this);

    [RelayCommand]
    private Task Duplicate() => _duplicate(this);

    [RelayCommand]
    private void OpenFolder() => ShellOpen.Directory(World.DirectoryPath);
}

/// <summary>One saved server with its live status.</summary>
public sealed partial class ServerItemViewModel : ObservableObject
{
    private readonly Func<ServerItemViewModel, Task> _remove;

    public ServerItemViewModel(ServerEntry entry, Func<ServerItemViewModel, Task> remove)
    {
        Entry = entry;
        _remove = remove;
    }

    public ServerEntry Entry { get; }

    public string Name => Entry.Name;

    public string Address => Entry.Address;

    [ObservableProperty]
    private ServerStatus? _status;

    public string StatusText => Status switch
    {
        null => "not pinged yet",
        { Online: false } status => status.Error ?? "offline",
        var status => $"{status.VersionName} · {status.PlayerCountText} players · {status.LatencyText}",
    };

    public string? Motd => Status?.Motd;

    public bool HasMotd => !string.IsNullOrWhiteSpace(Motd);

    partial void OnStatusChanged(ServerStatus? value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Motd));
        OnPropertyChanged(nameof(HasMotd));
    }

    [RelayCommand]
    private Task Remove() => _remove(this);

    /// <summary>Quick play passes "host:port", which the game accepts directly.</summary>
    public string JoinAddress => Address.Contains(':') ? Address : Address + ":25565";
}
