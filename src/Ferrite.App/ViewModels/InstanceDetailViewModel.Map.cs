using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Game;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The world map: a rendering of the chunks a world actually has, with chunk selection and the two
/// edits that need it - deleting the selected chunks, and copying them into another world.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    private readonly HashSet<(int X, int Z)> _selectedChunks = [];
    private WorldMap? _renderedMap;

    [ObservableProperty]
    private Bitmap? _worldMapImage;

    [ObservableProperty]
    private WorldItemViewModel? _mapWorld;

    [ObservableProperty]
    private WorldItemViewModel? _mapCopyTarget;

    [ObservableProperty]
    private string? _mapStatus;

    [ObservableProperty]
    private bool _isMapping;

    public bool HasWorldMap => WorldMapImage is not null;

    public bool HasMapStatus => !string.IsNullOrEmpty(MapStatus);

    public bool HasChunkSelection => _selectedChunks.Count > 0;

    public string MapSelectionText => Localizer.Format("L.Instance.MapSelection", _selectedChunks.Count);

    partial void OnMapStatusChanged(string? value) => OnPropertyChanged(nameof(HasMapStatus));

    /// <summary>Points the pickers at the instance's worlds, preferring a different copy target.</summary>
    private void LoadMapWorlds()
    {
        MapWorld ??= Worlds.FirstOrDefault();
        MapCopyTarget ??= Worlds.FirstOrDefault(world => !ReferenceEquals(world, MapWorld))
                          ?? Worlds.FirstOrDefault();
    }

    /// <summary>Renders the chosen world and draws the current selection over it.</summary>
    [RelayCommand]
    private async Task RefreshMapAsync()
    {
        if (IsMapping)
        {
            return;
        }

        LoadMapWorlds();
        if (MapWorld?.World.DirectoryPath is not { Length: > 0 } directory)
        {
            MapStatus = Localizer.Get("L.Instance.MapNoWorlds");
            return;
        }

        IsMapping = true;
        try
        {
            _shell.BeginActivity(Localizer.Get("L.Instance.MapRendering"));
            var map = await Task.Run(
                    () => _services.WorldMaps.Render(directory, CancellationToken.None),
                    CancellationToken.None)
                .ConfigureAwait(true);
            _renderedMap = map;
            _selectedChunks.Clear();
            ComposeMapImage();
            MapStatus = map.PresentChunks == 0
                ? Localizer.Get("L.Instance.MapEmpty")
                : Localizer.Format("L.Instance.MapRendered", map.PresentChunks, map.TotalChunks);
        }
        catch (Exception exception)
        {
            MapStatus = exception.Message;
        }
        finally
        {
            IsMapping = false;
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Turns a click on the rendered image into a chunk selection. The image is shown at its own pixel
    /// size, so a pixel of the image is a pixel of the map.
    /// </summary>
    public void ToggleChunkAt(int pixelX, int pixelZ)
    {
        if (_renderedMap?.ChunkAt(pixelX, pixelZ) is not { } chunk)
        {
            return;
        }

        if (!_selectedChunks.Add(chunk))
        {
            _selectedChunks.Remove(chunk);
        }

        ComposeMapImage();
    }

    [RelayCommand]
    private void ClearChunkSelection()
    {
        _selectedChunks.Clear();
        ComposeMapImage();
    }

    /// <summary>Deletes the selected chunks, which the editor backs up the region files for first.</summary>
    [RelayCommand]
    private async Task DeleteSelectedChunksAsync()
    {
        if (_selectedChunks.Count == 0)
        {
            MapStatus = Localizer.Get("L.Instance.MapNoSelection");
            return;
        }

        if (MapWorld?.World.DirectoryPath is not { Length: > 0 } directory)
        {
            return;
        }

        try
        {
            _shell.BeginActivity(Localizer.Get("L.Instance.MapDeleting"));
            var selection = _selectedChunks.ToList();
            var result = await Task.Run(
                    () => _services.WorldChunks.DeleteChunks(directory, selection, CancellationToken.None),
                    CancellationToken.None)
                .ConfigureAwait(true);
            _selectedChunks.Clear();
            MapStatus = Localizer.Format("L.Instance.MapDeleted", result.ChunksAffected);

            // Re-render from disk so the map shows what is actually left.
            await RefreshMapAsync().ConfigureAwait(true);
            MapStatus = Localizer.Format("L.Instance.MapDeleted", result.ChunksAffected);
        }
        catch (Exception exception)
        {
            MapStatus = exception.Message;
        }
        finally
        {
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Copies the selected chunks into another world at the same coordinates, rewriting each chunk's
    /// own position so the game can read it there.
    /// </summary>
    [RelayCommand]
    private async Task CopySelectedChunksAsync()
    {
        if (_selectedChunks.Count == 0)
        {
            MapStatus = Localizer.Get("L.Instance.MapNoSelection");
            return;
        }

        if (MapWorld?.World.DirectoryPath is not { Length: > 0 } source)
        {
            return;
        }

        if (MapCopyTarget?.World.DirectoryPath is not { Length: > 0 } target
            || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            MapStatus = Localizer.Get("L.Instance.MapCopyTarget");
            return;
        }

        try
        {
            _shell.BeginActivity(Localizer.Get("L.Instance.MapCopying"));
            var selection = _selectedChunks.ToList();
            var result = await Task.Run(
                    () => _services.WorldChunks.CopyChunks(
                        source,
                        target,
                        selection,
                        offsetX: 0,
                        offsetZ: 0,
                        CancellationToken.None),
                    CancellationToken.None)
                .ConfigureAwait(true);
            MapStatus = Localizer.Format(
                "L.Instance.MapCopied",
                result.ChunksAffected,
                MapCopyTarget!.Name);
        }
        catch (Exception exception)
        {
            MapStatus = exception.Message;
        }
        finally
        {
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Copies the rendered pixels into a bitmap, tinting the selected chunks. Compositing here rather
    /// than with overlays means the selection lines up with the map at any zoom.
    /// </summary>
    private void ComposeMapImage()
    {
        if (_renderedMap is not { Width: > 0, Height: > 0 } map)
        {
            WorldMapImage = null;
            OnPropertyChanged(nameof(HasWorldMap));
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(map.Width, map.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[map.Bgra.Length];
            Array.Copy(map.Bgra, pixels, pixels.Length);

            foreach (var (chunkX, chunkZ) in _selectedChunks)
            {
                var startX = (((chunkX - map.MinChunkX) * 16) / map.Step);
                var startZ = (((chunkZ - map.MinChunkZ) * 16) / map.Step);
                var size = Math.Max(1, 16 / map.Step);
                for (var offsetZ = 0; offsetZ < size; offsetZ++)
                {
                    for (var offsetX = 0; offsetX < size; offsetX++)
                    {
                        var pixelX = startX + offsetX;
                        var pixelZ = startZ + offsetZ;
                        if (pixelX < 0 || pixelZ < 0 || pixelX >= map.Width || pixelZ >= map.Height)
                        {
                            continue;
                        }

                        var index = ((pixelZ * map.Width) + pixelX) * 4;
                        // A copper tint, so a selected chunk is unmistakable against the map's blues.
                        pixels[index] = (byte)(pixels[index] / 3);
                        pixels[index + 1] = (byte)(pixels[index + 1] / 2);
                        pixels[index + 2] = 210;
                    }
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        WorldMapImage = bitmap;
        OnPropertyChanged(nameof(HasWorldMap));
        OnPropertyChanged(nameof(HasChunkSelection));
        OnPropertyChanged(nameof(MapSelectionText));
    }
}
