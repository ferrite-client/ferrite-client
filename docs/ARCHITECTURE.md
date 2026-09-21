# Architecture

## 1. Shape of the solution

```
Ferrite.slnx
  src/Ferrite.Core      class library, net10.0, no UI dependencies
  src/Ferrite.App       Avalonia 12 desktop app, net10.0, WinExe
  tests/Ferrite.Core.Tests  xUnit
```

Dependency direction is strictly one way: `Ferrite.App` -> `Ferrite.Core`. Core never
references Avalonia. Every service that performs work (network, disk, process) lives in Core
and is testable without starting a UI.

`Ferrite.Core` uses folder-as-namespace organisation rather than many tiny projects; the
brief warns against unnecessary projects and unnecessary interfaces.

## 2. Core module map

| Folder | Responsibility |
| --- | --- |
| `Platform/` | Data-root layout, path resolution, platform facts (OS, arch, Windows version) |
| `Storage/` | Atomic JSON persistence, settings, instance store, account store |
| `Json/` | Serialisation helpers, converters, manifest and version-document models |
| `Rules/` | OS/arch/feature rule evaluation for Mojang argument and library rules |
| `Download/` | HTTP client factory, bounded download engine, hashing, atomic finalisation |
| `Minecraft/` | Version manifest, version documents, inheritance, asset indexes, installer, launch |
| `Java/` | Discovery, detection, compatibility, provisioning |
| `Auth/` | Microsoft OAuth, Xbox Live, XSTS, Minecraft services, credential protection |
| `Loaders/` | Fabric, Quilt, NeoForge, Forge, OptiFine |
| `Content/` | Mod parsing, instance content scanning, Modrinth, CurseForge, modpacks |
| `Game/` | NBT reader, worlds, servers, server ping |
| `Diagnostics/` | Logging, log/crash parsing, repair, diagnostics bundle |

## 3. Key design decisions

**Instance-first.** The domain root is the instance. There is no global `.minecraft` that the
product revolves around; each instance owns a game directory and its own settings, while
immutable artefacts (libraries, assets, runtimes, client JARs) live in a shared store.

**Two-tier storage.** A shared immutable store under the data root plus per-instance mutable
directories. This gives the disk-usage benefit XMCL gets from hard links while keeping each
instance individually repairable: if a store file is damaged it is simply re-downloaded, and
instances never depend on link semantics for correctness.

**JSON documents, not a database.** Launcher state is small and must be human-inspectable and
repairable after a crash: instances, accounts, settings, and per-instance manifests are JSON
files written atomically (temp file + flush + rename). Large collections (mods) are derived
from disk scanning plus a cache rather than hand-maintained tables, so the filesystem stays
the source of truth.

**Cancellation everywhere.** Every long operation takes a `CancellationToken`. Downloads,
installs, scans, and launches are all cancellable and leave no half-written artefacts thanks
to atomic finalisation.

**No shell strings.** Processes (Java, installers) are started with
`ProcessStartInfo.ArgumentList`, so arguments can never be reinterpreted by a shell.

**Untrusted by default.** Archives, remote JSON, filenames, and URLs are validated. Every
extraction path is checked for containment inside the intended destination.

## 4. Persistence layout

```
%APPDATA%/Ferrite/                 (launcher root, movable)
  config/
    settings.json                  launcher settings (schema-versioned)
    accounts.json                  account records (tokens encrypted per record)
    accounts.bin                   DPAPI-protected token blobs and provider API keys
  data/
    instances/<id>/instance.json   instance metadata
    instances/<id>/.minecraft/     game directory (isolated)
    cache/                         version manifests, provider responses, metadata
    store/
      libraries/                   maven layout, immutable, hash-verified
      assets/objects/              content-addressed by hash
      assets/indexes/              asset index JSON
      versions/<version>/          client JAR and version document
      runtimes/<component>/        provisioned Java runtimes
  logs/
    launcher/                      structured JSON-lines launcher logs
  tmp/                             in-flight downloads and extraction
  backups/                         instance backups before destructive operations
```

## 5. Download pipeline

1. A caller submits a set of `DownloadRequest` items (URL, target, expected hash, size).
2. The engine groups identical targets, skips items already satisfying their hash, and
   schedules the remainder through a bounded worker pool.
3. Each transfer writes to `tmp/<guid>.part`, optionally resuming with a `Range` request.
4. On completion the hash is verified, then the file is moved atomically into the store.
5. Failures retry with exponential backoff and jitter; permanent failures leave no partial
   file in the destination.
6. Aggregate and per-file progress (bytes, totals, rate, ETA) are reported through
   `IProgress<DownloadProgress>`.

## 6. Minecraft installation

1. Resolve the version manifest entry, then the version document, with inheritance merged
   depth-first (`inheritsFrom` chains are short but must be handled).
2. Evaluate `rules` for every library and argument against the host OS/arch/features.
3. Build the artefact set: client JAR, libraries, natives, asset index, assets, logging config.
4. Feed the set to the download engine, then extract natives into the instance's native
   directory, honouring `extract.exclude`.
5. Record an install manifest per instance so repair can detect missing or corrupt files
   without re-fetching remote metadata.

## 7. Launch pipeline

1. Preflight: account validity, Java selection and compatibility, required files present,
   memory within host limits.
2. Build the classpath: resolved libraries in order, then the client JAR.
3. Build the argument vector: `arguments.jvm` (platform defaults + user overrides), then
   `mainClass`, then `arguments.game`, substituting placeholders.
4. Substitute all `${...}` placeholders, and fail fast with a named error on an unknown one.
5. Start the process with `ArgumentList` and the instance working directory.
6. Stream stdout/stderr into the launcher log, track the process, and publish exit code.

## 8. UI architecture

- MVVM with `CommunityToolkit.Mvvm` source-generated observables.
- A `MainWindow` with a compact rail navigation and a content host, plus a persistent
  activity/status strip for long operations.
- View models depend on Core services through an explicit composition root
  (`ServiceCollection`) built in `App`. No service location, no static singletons.
- Design language: graphite neutrals with a copper accent and a restrained teal for success
  states; own icon set; no copied launcher assets.
- The UI never performs I/O on the UI thread; view models marshal results back through the
  dispatcher.

## 9. Market/content services

- Providers implement `IContentProvider` (`Name`, `IsConfigured`, `UnavailableReason`,
  `SearchAsync`, `GetProjectAsync`, `GetVersionsAsync`, `GetVersionAsync`,
  `SelectBestVersion`). `ModrinthClient` and `CurseForgeClient` are the two implementations,
  so the browser, `ContentInstaller`, and the dependency resolver are provider-agnostic.
  `AppServices.InstallerFor(provider)` hands the browser the matching installer.
- Compatibility rules live in one place (`ContentCompatibility`): a version whose game version
  or loader does not match the instance is never selected, and provider token spellings
  (`NeoForge` vs `neoforge`) normalise before comparison.
- `CurseForgeClient` keeps its key behind a `Func<string?>` accessor reading
  `ProviderCredentialStore`, so a key entered in Settings takes effect without rebuilding the
  client. CurseForge addresses files per project, so `GetVersionAsync` takes the project id and
  `GetFilesAsync` resolves many file ids in one bulk request.
- Modpack installers: `MrpackInstaller` (`.mrpack`) and `CurseForgePackInstaller`
  (`manifest.json` + overrides). Both delegate instance resolution, loader installation, and
  the pre-install backup to `ModpackInstallSupport`, so the two formats cannot drift apart.
  `ModpackArchives.DetectKind` picks the installer from the archive's root entry, which is what
  the library import button and the browser's modpack install path use.
- The dependency resolver performs a breadth-first walk with a visited set, separating
  required from optional dependencies, and never installs a version whose loader or game
  version does not match the instance.

## 10. Diagnostics

- `CrashReportParser` reads a crash report as text: headline fields, stack frames, the report's own
  mod list, and system details. The format is produced by the game rather than specified, so the
  parser recognises the section shapes used by vanilla, Fabric, Forge, and NeoForge and ignores
  anything it does not know. Identical frames are collapsed because the same trace appears both
  inline and under `-- Head --`.
- `CrashAnalyzer` keeps two signals apart: what the report claims (its `Suspected Mods`) and what
  the stack frames actually reference (package segments matched against installed mod ids, jar
  names, and their parts). Frames under game, loader, and JDK package roots are never attributed to
  a mod. The rendered output says plainly that a frame is evidence that code ran, not proof of
  cause.
- `OperationLog` records what the launcher did: label, outcome, start time, and duration, one JSON
  object per line, bounded in memory and trimmed on disk. `MainWindowViewModel` opens a scope in
  `BeginActivity` and closes it in `EndActivity`/`ReportError`, so every existing action is covered
  by one integration point instead of per-feature instrumentation.
- `DiagnosticsBundleExporter` writes a support zip: system report, user notes, operation history,
  launcher logs, and — when an instance is supplied — its metadata, game logs, crash reports, and
  the analysis of the newest crash. Entry names are chosen by the exporter, so a hostile file name
  inside an instance cannot escape the archive, and every text entry passes through
  `SecretRedactor` before it is written.
