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
- Java discovery across PATH, vendor installs, and the Minecraft launcher's own runtimes.

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
