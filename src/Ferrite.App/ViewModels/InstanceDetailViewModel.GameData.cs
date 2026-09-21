using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Game;

namespace Ferrite.App.ViewModels;

/// <summary>Worlds and servers for one instance.</summary>
public sealed partial class InstanceDetailViewModel
{
    public ObservableCollection<WorldItemViewModel> Worlds { get; } = [];

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    public ObservableCollection<LanWorldItemViewModel> LanWorlds { get; } = [];

    [ObservableProperty]
    private string? _newServerName;

    [ObservableProperty]
    private string? _newServerAddress;

    [ObservableProperty]
    private string? _worldStatus;

    [ObservableProperty]
    private bool _isLanScanning;

    [ObservableProperty]
    private string? _lanStatus;

    public bool HasLanWorlds => LanWorlds.Count > 0;

    public bool HasWorlds => Worlds.Count > 0;

    public bool HasServers => Servers.Count > 0;

    /// <summary>
    /// Starts or stops listening for "Open to LAN" broadcasts. The listener is shared, so it is
    /// always stopped when the detail page closes.
    /// </summary>
    [RelayCommand]
    private void ToggleLanScan()
    {
        if (IsLanScanning)
        {
            StopLanScan(Localizer.Get("L.Instance.LanStopped"));
            return;
        }

        _services.LanWorlds.Changed += OnLanWorldsChanged;
        _services.LanWorlds.Start();

        if (!_services.LanWorlds.IsListening)
        {
            _services.LanWorlds.Changed -= OnLanWorldsChanged;
            LanStatus = Localizer.Format("L.Instance.LanUnavailable", _services.LanWorlds.FailureReason);
            return;
        }

        IsLanScanning = true;
        LanStatus = Localizer.Get("L.Instance.LanSearching");
        RefreshLanWorlds();
    }

    /// <summary>Stops listening and detaches. Called when the page closes.</summary>
    public void StopLanScan(string? status = null)
    {
        if (!IsLanScanning)
        {
            return;
        }

        _services.LanWorlds.Changed -= OnLanWorldsChanged;
        _services.LanWorlds.Stop();
        IsLanScanning = false;
        LanWorlds.Clear();
        OnPropertyChanged(nameof(HasLanWorlds));
            if (status is { Length: > 0 })
            {
                LanStatus = status;
            }
    }

    private void OnLanWorldsChanged()
    {
        // The listener runs on a background thread, so the collection is rebuilt on the UI thread.
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RefreshLanWorlds();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLanWorlds);
    }

    private void RefreshLanWorlds()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LanWorlds.Clear();
        foreach (var world in _services.LanWorlds.Current)
        {
            seen.Add(world.Address);
            LanWorlds.Add(new LanWorldItemViewModel(world, AddLanWorldAsync));
        }

        OnPropertyChanged(nameof(HasLanWorlds));
        if (IsLanScanning && LanWorlds.Count == 0)
        {
            LanStatus = Localizer.Get("L.Instance.LanNone");
        }
    }

    private async Task AddLanWorldAsync(LanWorldItemViewModel item)
    {
        try
        {
            _services.Servers.Add(
                GameDirectory,
                new ServerEntry { Name = item.Name, Address = item.Address });
            RefreshServers();
            item.Note = Localizer.Get("L.Instance.LanAdded");
            LanStatus = item.Note;
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    public async Task RefreshWorldsAsync()
    {
        try
        {
            var worlds = await Task.Run(
                    () => _services.Worlds.ListWorlds(GameDirectory, CancellationToken.None),
                    CancellationToken.None)
                .ConfigureAwait(true);

            Worlds.Clear();
            foreach (var world in worlds)
            {
                Worlds.Add(new WorldItemViewModel(world, BackupWorldAsync, DeleteWorldAsync, DuplicateWorldAsync));
            }

            OnPropertyChanged(nameof(HasWorlds));
        }
        catch (Exception exception)
        {
            WorldStatus = exception.Message;
        }
    }

    public void RefreshServers()
    {
        try
        {
            Servers.Clear();
            foreach (var entry in _services.Servers.Read(GameDirectory))
            {
                Servers.Add(new ServerItemViewModel(entry, RemoveServerAsync));
            }

            OnPropertyChanged(nameof(HasServers));
        }
        catch (Exception exception)
        {
            WorldStatus = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshGameDataAsync()
    {
        await RefreshWorldsAsync().ConfigureAwait(true);
        RefreshServers();
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewServerAddress))
        {
            WorldStatus = Localizer.Get("L.Instance.AddServerFirst");
            return;
        }

        try
        {
            var address = NewServerAddress.Trim();
            _services.Servers.Add(
                GameDirectory,
                new ServerEntry
                {
                    Name = string.IsNullOrWhiteSpace(NewServerName) ? address : NewServerName.Trim(),
                    Address = address,
                });

            NewServerName = null;
            NewServerAddress = null;
            RefreshServers();
            WorldStatus = Localizer.Get("L.Instance.ServerAdded");
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    [RelayCommand]
    private async Task PingServersAsync()
    {
        foreach (var server in Servers)
        {
            server.Status = await _services.Pinger
                .PingAsync(server.Address, null, TimeSpan.FromSeconds(8), CancellationToken.None)
                .ConfigureAwait(true);
        }
    }

    private async Task RemoveServerAsync(ServerItemViewModel server)
    {
        try
        {
            _services.Servers.Remove(GameDirectory, server.Address);
            RefreshServers();
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    private async Task BackupWorldAsync(WorldItemViewModel item)
    {
        try
        {
            var path = await _services.WorldsArchive
                .BackupAsync(item.World.DirectoryPath, CancellationToken.None)
                .ConfigureAwait(true);
            item.Note = Localizer.Format("L.Instance.BackedUp", Path.GetFileName(path));
            WorldStatus = item.Note;
        }
        catch (Exception exception)
        {
            item.Note = exception.Message;
        }
    }

    private async Task DeleteWorldAsync(WorldItemViewModel item)
    {
        try
        {
            var moved = _services.WorldsArchive.Delete(item.World.DirectoryPath);
            WorldStatus = Localizer.Format("L.Instance.WorldMoved", item.Name, Path.GetFileName(moved));
            await RefreshWorldsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            item.Note = exception.Message;
        }
    }

    private async Task DuplicateWorldAsync(WorldItemViewModel item)
    {
        try
        {
            var copy = _services.WorldsArchive.Duplicate(item.World.DirectoryPath, GameDirectory, null);
            WorldStatus = Localizer.Format("L.Instance.WorldDuplicated", Path.GetFileName(copy));
            await RefreshWorldsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            item.Note = exception.Message;
        }
    }
}
