using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;

namespace Ferrite.App.ViewModels;

/// <summary>Version and loader catalogue loading plus instance creation.</summary>
public sealed partial class LibraryViewModel
{
    private async Task LoadVersionsAsync()
    {
        try
        {
            var manifest = await _services.Manifest
                .GetManifestAsync(forceRefresh: false, CancellationToken.None)
                .ConfigureAwait(true);

            var wanted = ShowSnapshots ? new[] { "release", "snapshot" } : ["release"];
            Versions.Clear();
            foreach (var entry in manifest.Versions.Where(entry =>
                         wanted.Contains(entry.Type, StringComparer.OrdinalIgnoreCase)))
            {
                Versions.Add(entry);
            }

            NewVersion = Versions.FirstOrDefault(entry => entry.Id == manifest.Latest?.Release)
                ?? Versions.FirstOrDefault();
        }
        catch (Exception exception)
        {
            FormError = $"Version list unavailable: {exception.Message}";
        }
    }

    private async Task LoadLoaderVersionsAsync()
    {
        LoaderVersions.Clear();
        NewLoaderVersion = null;
        if (NewLoader == LoaderKind.Vanilla || NewVersion is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            FormError = null;
            var versions = NewLoader switch
            {
                LoaderKind.Fabric => await _services.Fabric
                    .ListFabricLoadersAsync(NewVersion.Id, CancellationToken.None).ConfigureAwait(true),
                LoaderKind.Quilt => await _services.Fabric
                    .ListQuiltLoadersAsync(NewVersion.Id, CancellationToken.None).ConfigureAwait(true),
                LoaderKind.NeoForge => await _services.Forge
                    .ListNeoForgeAsync(NewVersion.Id, CancellationToken.None).ConfigureAwait(true),
                LoaderKind.Forge => await _services.Forge
                    .ListForgeAsync(NewVersion.Id, CancellationToken.None).ConfigureAwait(true),
                _ => [],
            };

            foreach (var version in versions)
            {
                LoaderVersions.Add(version);
            }

            NewLoaderVersion = LoaderVersions.LastOrDefault(version => version.Stable)
                ?? LoaderVersions.LastOrDefault();

            if (LoaderVersions.Count == 0)
            {
                FormError = $"No {NewLoader.ToDisplayName()} build is published for Minecraft {NewVersion.Id}.";
            }
        }
        catch (Exception exception)
        {
            FormError = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (NewVersion is null)
        {
            FormError = Localizer.Get("L.Library.ChooseMinecraftVersion");
            return;
        }

        if (NewLoader != LoaderKind.Vanilla && NewLoaderVersion is null)
        {
            FormError = Localizer.Get("L.Library.ChooseLoaderVersion");
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewName) ? "New instance" : NewName.Trim();
        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Creating {name}...");

            var record = await CreateInstanceRecordAsync(name).ConfigureAwait(true);

            var versionId = InstanceLauncher.LaunchVersionId(record);
            if (NewLoader is not LoaderKind.Vanilla && NewLoaderVersion is not null)
            {
            _shell.ReportStatus(Localizer.Format(
                "L.Library.InstallingLoader",
                NewLoader.ToDisplayName(),
                NewLoaderVersion.Version));
                var java = JavaSelection.SelectBest(
                    await _services.Java.DetectAsync(CancellationToken.None).ConfigureAwait(true),
                    JavaCompatibility.RequiredMajorFor(NewVersion.Id));

                if (NewLoader is LoaderKind.Fabric or LoaderKind.Quilt)
                {
                    await _services.Fabric.InstallAsync(
                        NewLoader,
                        NewVersion.Id,
                        NewLoaderVersion.Version,
                        CancellationToken.None).ConfigureAwait(true);
                }
                else
                {
                    if (java is null)
                    {
                        FormError = $"Installing {NewLoader.ToDisplayName()} needs a compatible Java runtime. "
                            + "Install one from the Java page first.";
                        return;
                    }

                    await _services.Forge.InstallAsync(
                        NewLoader,
                        NewVersion.Id,
                        NewLoaderVersion.Version,
                        java,
                        new Progress<InstallProgress>(_shell.ReportActivity),
                        CancellationToken.None).ConfigureAwait(true);
                }
            }

            await _services.Installer.InstallAsync(
                record.Id,
                versionId,
                RuleContext.ForHost(),
                new Progress<InstallProgress>(_shell.ReportActivity),
                CancellationToken.None).ConfigureAwait(true);

            IsCreating = false;
            _shell.ReportStatus(Localizer.Format("L.Library.InstanceReady", name));
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            FormError = exception.Message;
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Writes the instance's metadata from the form: the chosen version, loader, and loader version.
    /// Separate from the install that follows so the persistence half can be checked on its own.
    /// </summary>
    internal async Task<InstanceRecord> CreateInstanceRecordAsync(string? name = null)
    {
        var record = new InstanceRecord
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name ?? NewName) ? "New instance" : (name ?? NewName)!.Trim(),
            MinecraftVersion = NewVersion?.Id ?? string.Empty,
            Loader = NewLoader,
            LoaderVersion = NewLoaderVersion?.Version,
        };

        return await _services.Instances.CreateAsync(record, CancellationToken.None).ConfigureAwait(true);
    }
}
