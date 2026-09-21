# Ferrite

A native Windows desktop launcher for modded Minecraft, written in C# on .NET 10 with
Avalonia. Ferrite is an original product: its architecture, code, and visual identity are its
own. XMCL is used only as a functional reference for what a current, complete launcher does.

## Status

Working launcher, under continued construction. `FEATURE_PARITY.md` is the authoritative statement
of what is implemented, verified, or externally blocked. Do not trust a summary over that file.

What has been exercised end to end against live services (evidence in `docs/VERIFICATION.md`):

- Installing Minecraft 26.3 from Mojang metadata: 5,223 files, 584.6 MiB, hash-verified.
- Launching it to the main menu: LWJGL 3.4.3, OpenGL via the installed NVIDIA driver, sound engine
  started, clean shutdown.
- Integrity verification and repair of a deliberately corrupted library.
- Installing and launching **Fabric** 0.19.5 on 1.21.1, and **NeoForge** 21.1.251 on 1.21.1 by
  running the official installer processor chain.
- Installing a real mod from Modrinth with loader-aware version selection, reading its metadata
  from the mod file, and disabling/re-enabling it without touching its bytes.
- Installing a real Modrinth modpack (Fabulously Optimized 6.5.0) into its own instance,
  launching it with 48 mods mounted by Fabric, exporting it, and re-importing the export.
- Reading worlds and `servers.dat` from a real Minecraft installation and backing a world up.
- Pinging production Minecraft servers for version, player counts, latency, and MOTD.
- Parsing a real crash report, listing the mods it names, matching stack frames to installed mods
  without overclaiming, and exporting a redacted support bundle with the launcher's operation
  history.
- Updating content the launcher installed: a real Sodium 0.5.11 install was updated to 0.8.13, with
  the old file removed only after the replacement was hash-verified.
- Publishing and consuming a signed update feed, and applying a staged build through the generated
  hand-off script.
- Finding worlds other players have opened to LAN, straight from the game's own announcement
  broadcast, and adding one to an instance's server list in a click.
- Reading what each installed resource pack declares and comparing it with the format the instance's
  own client file uses, so a pack the game will ignore is labelled rather than silently listed.
- A complete Polish interface alongside English, switchable in Settings without a restart.
- OptiFine support: Ferrite recognises the installer you download, runs it with the right Java in
  its own working directory, and adopts the resulting version into the launcher's store.
- Java discovery across PATH, vendor installs, and the Minecraft launcher's own runtimes.
- Browsing content with the provider's own facets: a category, loader, and game-version filter, a
  project panel with licence, tags, description and gallery, and the changelog of the version chosen.
- Managing what is installed in an instance: mods with search, filtering, ordering, and bulk actions;
  resource packs, shader packs, and per-world datapacks that can be disabled or removed (removal keeps
  the file in the launcher's backups); a screenshot gallery; and each world's own icon.
- Switching an instance's mod loader version, which installs the version, lays the instance out for
  it, and only then points the instance at it.
- Moving the launcher's data folder from Settings, with the choice remembered for the next start.
- Applying a modpack over an existing instance, with everything it replaces moved to a named backup.

Content browsing covers Modrinth and CurseForge through one browser. CurseForge needs a
user-issued API key; until one is stored, that provider reports a configuration state rather
than failing, and its live calls stay BLOCKED EXTERNAL in `FEATURE_PARITY.md`.

## Requirements

- Windows 11 x64
- .NET SDK 10 (build) or the self-contained package (run)
- Java is not required to be installed: Ferrite can provision a compatible runtime

## Build, test, run

```powershell
./scripts/build.ps1
./scripts/test.ps1
dotnet run --project src/Ferrite.App
```

Note: .NET 10's `dotnet test` integration does not discover xunit v3 tests here, so
`scripts/test.ps1` runs each test project through its runner entry point. See
`docs/DEVELOPMENT.md`.

## Package

```powershell
./scripts/package.ps1
```

Produces `artifacts/framework-dependent` (about 31 MiB, needs the .NET 10 desktop runtime) and
`artifacts/self-contained` (about 107 MiB, needs nothing), each with a zip beside it. Neither is
code-signed.

## Updates

Ferrite checks a feed you host. The feed is a directory containing `manifest.json`,
`manifest.json.sig`, and the package zips; the manifest is signed with a key whose public half is
built into the launcher. A build with no embedded key refuses to check rather than trusting an
unsigned feed.

```powershell
# Publish a release
pwsh -File scripts/package.ps1 -Version 0.2.0
dotnet run --project tools/Ferrite.Verify -- sign-update --version 0.2.0 `
  --package artifacts/ferrite-self-contained-win-x64.zip --out .\feed --kind self-contained

# Try it locally before hosting anything
dotnet run --project tools/Ferrite.Verify -- update-check --feed .\feed `
  --key .\feed\update-public-key.pem --current 0.1.0 --stage
```

Apply a staged update by running the command Ferrite prints under Settings -> Launcher updates; the
launcher never replaces its own running files. Full steps are in `docs/HUMAN_ACTION_REQUIRED.md` (H3).

## Data locations

```
%APPDATA%/Ferrite/
  config/     settings, accounts (tokens protected by the OS)
  data/       instances, shared library/asset store, caches, provisioned runtimes
  logs/       structured launcher logs
  tmp/        in-flight downloads
  backups/    backups made before destructive operations
```

The data root can be relocated from Settings.

## Credentials

- **Microsoft sign-in** requires an Azure public-client application ID. Ferrite does not bundle
  one. See `docs/HUMAN_ACTION_REQUIRED.md`.
- **CurseForge** requires a user-issued API key, entered under Settings -> Content providers.
  It is written to the OS-protected secret store (`config/accounts.bin`), not to
  `settings.json`. See `docs/HUMAN_ACTION_REQUIRED.md` (H2).

Neither secret is ever committed to this repository.

## Documentation

| File | Purpose |
| --- | --- |
| `FEATURE_PARITY.md` | Capability matrix against XMCL, with honest status |
| `docs/RESEARCH.md` | Primary-source research and its implications |
| `docs/ARCHITECTURE.md` | Module map, storage layout, pipelines |
| `docs/IMPLEMENTATION_PLAN.md` | Milestones and exit criteria |
| `docs/DECISIONS.md` | Decisions with rejected alternatives |
| `docs/SECURITY.md` | Threat model and security controls |
| `docs/VERIFICATION.md` | Recorded evidence for verified capabilities |
| `docs/DEVELOPMENT.md` | Build, test, package, conventions |
| `docs/HUMAN_ACTION_REQUIRED.md` | Externally blocked items and exact user steps |

## Troubleshooting

**The launcher refuses to start a version.** Open the instance, press Verify; if it reports damaged
files, press Repair. A version that is not installed at all is downloaded on the next launch.

**"No compatible Java runtime is installed."** Open the Java page: every discovered runtime is
listed, and a runtime can be added by path. The page also downloads a suitable runtime for a chosen
Minecraft version.

**The game does not open the world a quick-play launch asked for.** The launch is passed the
arguments the version document and the wiki describe, which is verified; the game's own response is
recorded as blocked, and `docs/HUMAN_ACTION_REQUIRED.md` H6 says exactly what to check.

**A modpack or a mod will not install from CurseForge.** That provider needs an API key: Settings ->
Content providers. Modrinth needs no key.

**Something went wrong and there is nothing useful on screen.** Settings -> Diagnostics writes a
support bundle: launcher logs with secrets redacted, the operation history, Java and platform
information, and the instance's own recent output.

**The launcher's data is on a drive that is filling up.** Settings -> Storage -> Move data folder
copies it elsewhere and remembers the choice for the next start.

**Windows shows a warning on first run.** The package is not code-signed; `docs/HUMAN_ACTION_REQUIRED.md`
H5 describes signing.
