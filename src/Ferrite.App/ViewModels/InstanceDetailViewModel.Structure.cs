using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Game;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>One structure file, with the label the picker shows instead of a full path.</summary>
public sealed record StructureFileItem(string Path, string Label);

/// <summary>
/// The structure preview: read a structure file, show it in three dimensions, list what it is made of,
/// and say whether the game this instance runs can load it.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    [ObservableProperty]
    private Bitmap? _structureImage;

    [ObservableProperty]
    private string? _structureStatus;

    [ObservableProperty]
    private string? _structureSummary;

    [ObservableProperty]
    private string? _structureMaterials;

    [ObservableProperty]
    private StructureCompatibility _structureCompatibility = StructureCompatibility.Unknown;

    [ObservableProperty]
    private StructureFileItem? _selectedStructureFile;

    public ObservableCollection<StructureFileItem> StructureFiles { get; } = [];

    /// <summary>Structures the instance's own worlds ship, so the common case needs no file picker.</summary>
    public bool HasStructureFiles => StructureFiles.Count > 0;

    public bool HasStructureImage => StructureImage is not null;

    public bool HasStructureSummary => !string.IsNullOrEmpty(StructureSummary);

    public bool HasStructureMaterials => !string.IsNullOrEmpty(StructureMaterials);

    public bool HasStructureStatus => !string.IsNullOrEmpty(StructureStatus);

    public string StructureCompatibilityText => StructureCompatibility switch
    {
        StructureCompatibility.SameVersion => Localizer.Get("L.Instance.StructureSameVersion"),
        StructureCompatibility.StructureIsOlder => Localizer.Get("L.Instance.StructureOlder"),
        StructureCompatibility.StructureIsNewer => Localizer.Get("L.Instance.StructureNewer"),
        StructureCompatibility.NoStructureVersion => Localizer.Get("L.Instance.StructureNoVersion"),
        StructureCompatibility.NoGameVersion => Localizer.Get("L.Instance.StructureNoGame"),
        _ => Localizer.Get("L.Instance.StructureUnknown"),
    };

    partial void OnStructureStatusChanged(string? value) => OnPropertyChanged(nameof(HasStructureStatus));

    partial void OnStructureSummaryChanged(string? value) => OnPropertyChanged(nameof(HasStructureSummary));

    partial void OnStructureMaterialsChanged(string? value) => OnPropertyChanged(nameof(HasStructureMaterials));

    partial void OnStructureCompatibilityChanged(StructureCompatibility value) =>
        OnPropertyChanged(nameof(StructureCompatibilityText));

    /// <summary>Finds the structures bundled with the instance's worlds.</summary>
    public async Task RefreshStructuresAsync()
    {
        try
        {
            var game = GameDirectory;
            var found = await Task.Run(() => FindStructures(game), CancellationToken.None).ConfigureAwait(true);
            StructureFiles.Clear();
            foreach (var path in found)
            {
                StructureFiles.Add(new StructureFileItem(path, ShortLabel(path)));
            }

            OnPropertyChanged(nameof(HasStructureFiles));
            SelectedStructureFile ??= StructureFiles.FirstOrDefault();
            if (StructureFiles.Count == 0 && StructureStatus is null)
            {
                StructureStatus = Localizer.Get("L.Instance.StructureNoneFound");
            }
        }
        catch (Exception exception)
        {
            StructureStatus = exception.Message;
        }
    }

    /// <summary>Structure files live under each world's generated folder, and in datapacks.</summary>
    private static IReadOnlyList<string> FindStructures(string gameDirectory)
    {
        var saves = Path.Combine(gameDirectory, "saves");
        if (!Directory.Exists(saves))
        {
            return [];
        }

        var found = new List<string>();
        foreach (var world in Directory.EnumerateDirectories(saves))
        {
            var generated = Path.Combine(world, "generated");
            if (Directory.Exists(generated))
            {
                found.AddRange(Directory.EnumerateFiles(generated, "*.nbt", SearchOption.AllDirectories));
            }

            var datapacks = Path.Combine(world, "datapacks");
            if (Directory.Exists(datapacks))
            {
                found.AddRange(Directory.EnumerateFiles(datapacks, "*.nbt", SearchOption.AllDirectories));
            }
        }

        return found.OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    [RelayCommand]
    private async Task LoadSelectedStructureAsync()
    {
        if (SelectedStructureFile is { Path.Length: > 0 } item)
        {
            await LoadStructureAsync(item.Path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The label a structure is listed under: its path below the structures folder, which is what tells
    /// two of them apart, instead of an absolute path that would wrap in the picker.
    /// </summary>
    private static string ShortLabel(string path)
    {
        var marker = Path.Combine("generated", "minecraft", "structures");
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var relative = path[(index + marker.Length)..].TrimStart('\\', '/');
            if (relative.Length > 0)
            {
                return relative.Replace('\\', '/');
            }
        }

        return Path.GetFileName(path);
    }

    /// <summary>
    /// Reads and previews one structure file. Called by the view after the user picks a file, so the
    /// view model never touches platform storage APIs.
    /// </summary>
    public async Task<bool> LoadStructureAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StructureStatus = Localizer.Get("L.Library.UnreadableFile");
            return false;
        }

        try
        {
            _shell.BeginActivity(Localizer.Get("L.Instance.StructureReading"));
            var layout = await Task.Run(() => StructureReader.ReadFile(path), CancellationToken.None)
                .ConfigureAwait(true);
            var image = await Task.Run(() => StructurePreview.Render(layout), CancellationToken.None)
                .ConfigureAwait(true);

            StructureImage = ToBitmap(image);
            OnPropertyChanged(nameof(HasStructureImage));

            StructureSummary = Localizer.Format(
                "L.Instance.StructureSummary",
                layout.SizeX,
                layout.SizeY,
                layout.SizeZ,
                layout.Blocks.Count,
                layout.Materials().Count);
            StructureMaterials = DescribeMaterials(layout);

            var clientJar = _services.Paths.VersionClientJarFile(InstanceLauncher.LaunchVersionId(Record));
            StructureCompatibility = StructureCompatibilityCheck.Evaluate(
                layout.DataVersion,
                StructureCompatibilityCheck.ReadClientDataVersion(clientJar));

            StructureStatus = Localizer.Format("L.Instance.StructureLoaded", Path.GetFileName(path));
            return true;
        }
        catch (Exception exception) when (exception is NbtException or IOException or InvalidDataException)
        {
            StructureStatus = exception.Message;
            return false;
        }
        finally
        {
            _shell.EndActivity();
        }
    }

    private static string DescribeMaterials(StructureLayout layout)
    {
        var builder = new StringBuilder();
        foreach (var (name, count) in layout.Materials()
                     .Where(entry => !StructureReader.IsAir(entry.Name))
                     .Take(12))
        {
            builder.AppendLine($"{count,5}  {StructureReader.StripNamespace(name)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static Bitmap? ToBitmap(StructureImage image)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Bgra.Length == 0)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(image.Bgra, 0, buffer.Address, image.Bgra.Length);
        }

        return bitmap;
    }
}
