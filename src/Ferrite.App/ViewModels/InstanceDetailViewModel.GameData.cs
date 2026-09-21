using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Game;

namespace Ferrite.App.ViewModels;

/// <summary>Worlds and servers for one instance.</summary>
public sealed partial class InstanceDetailViewModel
{
    public ObservableCollection<WorldItemViewModel> Worlds { get; } = [];

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    [ObservableProperty]
    private string? _newServerName;

    [ObservableProperty]
    private string? _newServerAddress;

    [ObservableProperty]
    private string? _worldStatus;

    public bool HasWorlds => Worlds.Count > 0;

    public bool HasServers => Servers.Count > 0;

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
            WorldStatus = "Enter a server address first.";
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
            WorldStatus = "Server added";
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
            item.Note = $"Backed up to {Path.GetFileName(path)}";
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
            WorldStatus = $"{item.Name} was moved to {Path.GetFileName(moved)}";
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
            WorldStatus = $"Duplicated to {Path.GetFileName(copy)}";
            await RefreshWorldsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            item.Note = exception.Message;
        }
    }
}
