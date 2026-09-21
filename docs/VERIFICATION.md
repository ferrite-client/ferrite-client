# Verification Log

Evidence for every `VERIFIED` status in `FEATURE_PARITY.md`. Each entry records the
environment, date, scenario, steps, result, and any limitation.

Environment used for verification unless stated otherwise:

- OS: Windows 11 x64
- .NET SDK: 10.0.201 (runtime 10.0.5)
- Java present on PATH: Temurin OpenJDK 25.0.3
- Existing Minecraft data directory: present at `%APPDATA%\.minecraft` (used read-only as a
  fixture source wherever that is useful)

---

## V001 - Vanilla vertical slice: install, verify, repair, launch (2026-09-20)

**Environment.** Windows 11 x64; .NET SDK 10.0.201; Temurin OpenJDK 25.0.3 on PATH; NVIDIA
driver 610.47; data root `%APPDATA%\Ferrite`.

**Harness.** `dotnet run --project tools/Ferrite.Verify -- <scenario>` - a console composition
root that wires the same Core services the launcher uses, against live Mojang endpoints.

### V001.1 Java discovery

Command: `Ferrite.Verify java`

Result: 6 usable runtimes discovered and probed by actually executing them.

| Version | Vendor | Source | Path |
| --- | --- | --- | --- |
| 25 | Eclipse Adoptium 25.0.3 | PATH | `C:\Program Files\Eclipse Adoptium\jdk-25.0.3.9-hotspot\bin\java.exe` |
| 25 | Microsoft 25.0.1 | Launcher runtime store | `...\Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\...\runtime\java-runtime-epsilon\windows-x64\...` |
| 21 | Eclipse Adoptium 21.0.10 | PATH | `C:\Program Files\Eclipse Adoptium\jdk-21.0.10.7-hotspot\bin\java.exe` |
| 17 | Eclipse Adoptium 17.0.18 | PATH | `C:\Program Files\Eclipse Adoptium\jdk-17.0.18.8-hotspot\bin\java.exe` |
| 17 | Microsoft 17.0.15 | Launcher runtime store | `...\runtime\java-runtime-gamma\windows-x64\...` |
| 8 | Oracle 1.8.0.51 | Launcher runtime store | `...\runtime\jre-legacy\windows-x64\...` |

Evidence: runtime store detection, vendor detection, major-version parsing, and executable
validation all work against real installations. Verifies E01, E02, E03, E09.

### V001.2 Version manifest

Command: `Ferrite.Verify manifest`

Result: 915 versions fetched from `piston-meta.mojang.com`; latest release and snapshot both
`26.3`; channel breakdown `release=103, snapshot=751, old_beta=26, old_alpha=35`; cached to
`data/cache/version_manifest_v2.json`. Verifies C01, C02.

### V001.3 Install Minecraft 26.3

Command: `Ferrite.Verify install 26.3`

Result:

```
Install complete in 23.7s
  files:     5223
  bytes:     584.6 MiB
  natives:   ...\instances\<id>\natives\26.3
  log4j cfg: ...\data\store\versions\26.3\client-1.21.2.xml
```

Observed sustained throughput of ~25 MiB/s with 8-way bounded concurrency. The run covered:
version manifest and version document retrieval, rule evaluation against the host, library
planning (including the 56 natives-classified entries in the 26.3 metadata), asset index
retrieval, 5,223 artefact downloads with SHA-1 verification and atomic finalisation, native
extraction, and the log4j configuration download. Verifies C03-C16, D01-D10.

### V001.4 Integrity verification

Command: `Ferrite.Verify verify 26.3`

Result: `Verified 5223 files (584.6 MiB) in 4.2s`, `Healthy: True`.

### V001.5 Repair

Command: `Ferrite.Verify repair 26.3`

Result: the harness overwrote a managed library with 64 bytes of zeros.

```
Damaged ...\store\libraries\at\yawk\lz4\lz4-java\1.10.1\lz4-java-1.10.1.jar (was 910.2 KiB)
Detection: 1 issue(s) found
[info] MinecraftInstaller: Repairing 1 file(s) for instance <id>
[info] DownloadEngine: Re-acquiring ...lz4-java-1.10.1.jar: existing file failed verification
Repair result: healthy=True, remaining issues=0
```

Verifies C11, C12, C13, and the repair half of N05.

### V001.6 Launch

Command: `Ferrite.Verify launch 26.3 --seconds 60`

Result: the game started, rendered, and was stopped cleanly after the observation window. The
harness reported `Signals: lwjgl=True, graphics=True, exitedEarly=False`.

Key lines from the game's own log
(`...\instances\<id>\minecraft\logs\ferrite-20260920-235113.log`):

```
Setting user: FerriteVerify
Backend library: LWJGL version 3.4.3+4
Using graphics backend OpenGL, using drivers: 3.3.0 NVIDIA 610.47
Reloading ResourceManager: vanilla, vanilla
Created: 2048x2048x4 minecraft:textures/atlas/blocks.png-atlas
Sound engine started
```

The launcher produced a 41-argument command with credentials redacted in the preview. The
`--uuid`, `--accessToken`, `--clientId` and `--xuid` values appeared as `***redacted***` in the
displayed command while the real arguments carried the values.

**Interpretation.** This is real evidence that the launcher builds a correct modern command,
starts a real JVM, loads LWJGL and native libraries, initialises the OpenGL renderer, loads the
vanilla asset set, and reaches the main menu. It verifies H01-H09 and H12, C06-C10, C17.

**Limitations, stated precisely.** The launch used a placeholder identity
(`FerriteVerify` / `verify-pipeline-token`), not a real Microsoft account, because no Azure
client ID exists in this environment (see `HUMAN_ACTION_REQUIRED.md` H1). The game therefore
logged `Failed to fetch user properties` and `Could not authorize you against Realms server`,
both of which are expected without a valid session. The harness account is a diagnostic fixture
inside the verification tool only; Ferrite has no offline account feature.

One benign error appeared in the game log: `oshi.driver.windows.registry.HkeyPerformanceDataUtil`
could not read the `Perflib 009` registry counters. This is a known Windows performance-counter
quirk reported by Minecraft's own dependency (OSHI) and does not affect play.

### V001.7 Command preview redaction

Verified in the V001.6 output: the displayed command replaced the access token, UUID, client id,
and XUID with `***redacted***`. Verifies F10 for the launch path.

---

## V002 - Mod loader vertical slice (2026-09-21)

Same environment and harness as V001.

### V002.1 Fabric 1.21.1

Commands: `Ferrite.Verify fabric 1.21.1`, then `Ferrite.Verify launch 1.21.1 --seconds 70`.

- Catalogue: 253 loader versions found for 1.21.1; the newest stable (`0.19.5`) was selected.
- Install: `3968 files, 877.6 MiB` (1.21.1 vanilla artefacts plus the Fabric loader profile
  libraries: fabric-loader, sponge-mixin, intermediary, ASM 9.10.1).
- Launch: the loader's own log reported
  `Loading Minecraft 1.21.1 with Fabric Loader 0.19.5`, then `Setting user: FerriteVerify`,
  `Backend library: LWJGL version 3.3.3-snapshot`,
  `Reloading ResourceManager: vanilla`, `Sound engine started`. The game ran the full window and
  was stopped cleanly; `Signals: lwjgl=True, graphics=True, exitedEarly=False`.

**Two real defects were found and fixed by this run**, both recorded in `docs/DECISIONS.md`:

1. `-DFabricMcEmu= net.minecraft.client.main.Main ` is a single JVM argument whose value contains
   spaces. Splitting argument values on whitespace moved the main class, so the JVM started the
   vanilla main class and reported `Completely ignored arguments` for the loader main class and
   the memory flag. Fixed by never splitting `-D` values (D010).
2. Java selection preferred the newest compatible runtime (Java 25) for a loader that requires
   Java 21, which produced a mixin classloader `ClassCastException`. Fixed by preferring the
   closest supported runtime (D011).

### V002.2 NeoForge 21.1.251

Commands: `Ferrite.Verify neoforge 1.21.1`, then `Ferrite.Verify launch 1.21.1 --seconds 75`.

- Catalogue: 352 NeoForge builds found for 1.21.1 via maven metadata.
- Installer: `neoforge-21.1.251-installer.jar` downloaded and its `install_profile.json` read.
- Processor chain: 6 client-side processors executed in order
  (`net.neoforged.installertools.ConsoleTool` for MCP data and Mojmaps,
  `net.neoforged.jarsplitter.ConsoleTool`, `net.neoforged.art.Main`,
  `net.neoforged.binarypatcher.ConsoleTool`). The two `sides: ["server"]` processors were skipped.
- Install: `3996 files, 882.7 MiB`.
- Launch: ModLauncher started with the NeoForge argument set, discovered
  `neoforge-21.1.251-universal.jar`, `mixinextras-neoforge-0.5.3.jar`, and
  `client-1.21.1-20240808.144430-srg.jar`, then
  `Reloading ResourceManager: vanilla, mod_resources, mod/neoforge`,
  `Backend library: LWJGL version 3.3.3+5`, `Sound engine started`. The game ran the full window
  and was stopped cleanly.

The NeoForge mod loader appearing in the resource manager output is direct evidence that a modded
instance reaches a working game, not merely a started JVM.

**Observation.** NeoForge's own ModLauncher redacted the access token in its log output
(`--accessToken, ❄❄❄❄❄❄❄❄`), which is independent confirmation that the token was passed as an
argument and that credential handling in third-party logging is worth not relying on.

---

## V003 - Content: Modrinth install, mod inventory, enable/disable (2026-09-21)

### V003.1 Search and loader-aware version selection

Command: `Ferrite.Verify content 1.21.1 --slug sodium`

The instance used was the NeoForge 21.1.251 instance created in V002.2. Ferrite selected
`sodium-neoforge-0.8.13+mc1.21.1.jar`, not the Fabric build, because version selection filters on
the instance's loader and game version. A Fabric-only project would have been refused rather than
silently installed.

### V003.2 Install and inventory

```
Plan: 1 file(s)
  mods/sodium-neoforge-0.8.13+mc1.21.1.jar (1.2 MiB)
Installed 1 file(s)
Mod inventory now reports 1 mod(s):
  Sodium [neoforge] id=sodium version=0.8.13+mc1.21.1 enabled=True
      depends on: minecraft, neoforge, embeddium
```

The metadata came from the mod's own `META-INF/neoforge.mods.toml`, not from the file name, and the
declared dependencies were extracted from it.

### V003.3 Enable / disable round trip

Command: `Ferrite.Verify toggle 1.21.1`

```
Toggling sodium-neoforge-0.8.13+mc1.21.1.jar
  disabled -> sodium-neoforge-0.8.13+mc1.21.1.jar.disabled
  metadata still readable while disabled: Sodium, enabled=False
  re-enabled -> sodium-neoforge-0.8.13+mc1.21.1.jar
  metadata after re-enable: Sodium, enabled=True
```

Disabling renames the file and never rewrites it, so the mod's bytes are untouched.

---

## V004 - Interface rendering and visual QA (2026-09-21)

**Method.** `tests/Ferrite.App.Tests` renders the real `MainWindow` and the real views through
Avalonia's headless Skia platform, captures frames, and asserts on the rendered visual tree. This
catches layout-time failures, missing bindings, and missing resources that a compile cannot. Frames
are written to `$env:FERRITE_UI_SHOTS` for inspection.

Commands: `pwsh -File scripts/test.ps1`

Result: `Ferrite.App.Tests Total: 5, Errors: 0, Failed: 0`.

Screens inspected: shell (dark), shell (light), shell at the 1024x680 minimum size, and every page
(Library, Browse, Java, Accounts, Settings) plus the instance detail page.

**Issues found and fixed during this pass**

1. Navigation used the theme's default radio glyphs, which read as form controls rather than
   navigation, and the selected row used Fluent's blue instead of Ferrite's copper accent. Replaced
   with a navigation list styled to the Ferrite palette.
2. The window-size row packed two numeric inputs and a separator into one narrow column, producing
   a cramped, misaligned row. Split into two equal fields with spinners hidden and a clarifying
   hint.
3. The Browse results pane and the detail pane were empty voids before any data arrived. Added
   empty-state copy that also explains how the filters are applied.

**Residual visual risks.** The interface has been reviewed at 1360x860, 1200x800 and 1024x680 in
both themes. Very long instance or mod names, large mod lists, and live progress states were
exercised only through the data model, not visually, because the environment has no populated
library; the layouts use wrapping text and scrolling containers so they degrade predictably.

---

## V005 - Modpack install, launch, export, and re-import (2026-09-21)

Pack used: **Fabulously Optimized 6.5.0** (`Fabulously.Optimized-v6.5.0.mrpack`, 177,589 bytes,
fetched from the Modrinth CDN). Its index declares `fabric-loader=0.19.3` and `minecraft=1.21.1`
with 50 files.

### V005.1 Install

Command: `Ferrite.Verify modpack --source <pack.mrpack>`

```
Pack: Fabulously Optimized 6.5.0 (format 1)
Dependencies: fabric-loader=0.19.3, minecraft=1.21.1
Declared files: 50
Instance: Fabulously Optimized (d7242875-977f-4a20-b801-0e47acfe63c6)
  launch version: fabric-loader-0.19.3-1.21.1
  files:          50 downloaded, 0 skipped
  overrides:      63
  mod inventory:  48 mod(s)
```

Every declared file was hash-verified (SHA-1 and SHA-512 from the index) and written to the
instance-relative path the pack specifies. The overrides tree was extracted with the prefix
stripped, and the mod inventory was then re-read from disk.

### V005.2 Launch the modpack instance

Command: `Ferrite.Verify launch 1.21.1 --instance "Fabulously Optimized" --seconds 80`

```
Launch version: fabric-loader-0.19.3-1.21.1
Loading Minecraft 1.21.1 with Fabric Loader 0.19.3
Reloading ResourceManager: vanilla, fabric, animatica, bettergrass, ... sodium, sodium-extra,
  iris, lithium, modmenu, ... (100+ entries)
Signals: lwjgl=True, graphics=True, exitedEarly=False
LaunchService: Instance ... exited with code 0 after 00:01:20.7
```

The resource-manager line lists the mods the loader actually mounted, so this is evidence of a
working modded game rather than a started JVM. The instance ran the full observation window and
exited cleanly.

### V005.3 Export and re-import

Command: `Ferrite.Verify export 1.21.1`

```
Exported Fabulously Optimized to ...\tmp\export-d7242875977f4a20b8010e47acfe63c6.mrpack
  version id: 6.5.0, overrides: 113
  size: 39.8 MiB
  re-read index: Fabulously Optimized 6.5.0, 2 dependency(ies)
Re-imported as Fabulously Optimized (imported): 113 override(s), 48 mod(s)
```

The exported archive is self-contained (content under `overrides/`, loader declared in
`dependencies`, `files[]` empty by design — see `ModpackExporter`), and re-importing it produced the
same 48 mods.

### V005.4 Defect found and fixed

The first install attempt failed with
`ArgumentException: The value cannot be an empty string` while extracting overrides. Real packs
contain a bare `overrides/` directory entry, which strips to an empty relative path and was being
passed to the path validator. Fixed by treating the prefix entry as the destination root, with a
regression test (`ExtractZip_ignores_the_prefix_directory_entry`).

---

## V006 - Worlds, NBT, and server status (2026-09-21)

### V006.1 Reading a real world

Command: `Ferrite.Verify worlds --game-dir %APPDATA%\.minecraft`

The existing Minecraft installation on this machine was read (read-only) as a fixture:

```
Reading worlds from C:\Users\wwmky\AppData\Roaming\.minecraft (read-only)
Found 1 world(s)
  New World [New World] creative v1.20.4 seed=5494849442907459678 8.4 MiB last=2025-09-22 19:56
      icon: C:\Users\wwmky\AppData\Roaming\.minecraft\saves\New World\icon.png
```

Every field came from the world's own gzip-compressed `level.dat`: display name, game mode,
version name and data version, seed, size on disk, last-played time, and the icon file. The seed
lives under `Data.RandomSeed` in older versions and `Data.WorldGenSettings.seed` in newer ones;
both are handled.

### V006.2 World backup

Command: `Ferrite.Verify backup-world --game-dir %APPDATA%\.minecraft`

```
Backed up world ...\.minecraft\saves\New World to ...\Ferrite\backups\world-New World-....zip
  size: 674.4 KiB
  entries: 33
  contains level.dat: True
```

Unit tests cover the rest of the lifecycle: restore (including moving a newer world aside rather
than overwriting it), duplicate, and delete-by-moving-to-backups.

### V006.3 Server status

Command: `Ferrite.Verify ping mc.hypixel.net play.cubecraft.net 2b2t.org`

```
  play.cubecraft.net:25565: CubeCraft 757/5000 126 ms
      CubeCraft Games [EU] / BEDWARS UPDATE: NEW ITEMS + MAPS
  mc.hypixel.net:25565: Requires MC 1.8 / 1.21 23644/200000 406 ms
      Hypixel Network [1.8/26.3]
      SKYBLOCK 0.27.1 TORRHUS & SAFARI
  2b2t.org: offline (The server did not respond in time.)
```

Two production servers answered the modern status exchange with version, player counts, latency,
and multi-line MOTDs; an unreachable host produced an offline status with a reason rather than an
exception.

### V006.4 Defect found and fixed

The NBT writer used `BinaryWriter`, which is little-endian, while NBT is big-endian. The first
world and server-list round trips failed with
`NbtException: The document ended after 6 byte(s) while 1792 more were needed` — a byte-swapped
string length. The writer now emits every multi-byte value big-endian explicitly. This mattered:
without the fix, `servers.dat` written by the launcher would have been unreadable by the game.

---

## V007 - CurseForge integration and shared modpack install path (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, `Ferrite.Verify` run against a scratch data root whose
`data/store` is a junction onto the real content store, so nothing in the user's library was
modified and no large artefacts were re-downloaded.

### V007.1 Automated tests

Command: `pwsh -File scripts/test.ps1`

```
   Ferrite.Core.Tests  Total: 137, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 25.2s
   Ferrite.App.Tests   Total: 5,   Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 1.3s
All test projects passed.
```

New coverage: `CurseForgeClientTests` (request shape with `gameId`/`classId`/`modLoaderType`,
response mapping, hashes and dependencies, missing-key configuration error, retail-file
`download-url: null`, id mappings), `CurseForgePackTests` (manifest parsing, loader resolution
for NeoForge/Forge/Fabric/Quilt, file planning into `mods/`, retail-file warning, path-escape
rejection, archive-kind detection, loader-token normalisation, release preference), and
`ProviderCredentialStoreTests` (round trip, clearing, no plaintext on disk, load idempotence).

### V007.2 `.mrpack` path re-verified after the shared-install refactor

`MrpackInstaller` lost its own instance/loader/backup helpers to `ModpackInstallSupport`, so
V005.1 was re-run end to end against the same pack:

Command: `Ferrite.Verify modpack https://cdn.modrinth.com/data/1KVo5zza/versions/N276l2ON/Fabulously.Optimized-v6.5.0.mrpack`

```
Pack: Fabulously Optimized 6.5.0 (format 1)
Dependencies: fabric-loader=0.19.3, minecraft=1.21.1
Declared files: 50
[info] MrpackInstaller: Installing modpack Fabulously Optimized 6.5.0 (Fabric 0.19.3, Minecraft 1.21.1)
Instance: Fabulously Optimized (fbd15019-7e65-4de1-b7fc-c2be2f2e2f7e)
  launch version: fabric-loader-0.19.3-1.21.1
  files:          50 downloaded, 0 skipped
  overrides:      63
  mod inventory:  48 mod(s)
```

Identical to V005.1, which is the evidence that the refactor preserved behaviour: 50
hash-verified files, 63 overrides, 48 mods read back from disk.

### V007.3 Modrinth search after the provider abstraction

Command: `Ferrite.Verify modrinth sodium`

```
Total hits: 351
  [Mod] Sodium (sodium) - 228,694,255 downloads
  [Mod] Sodium Extra (sodium-extra) - 96,965,977 downloads
  ...
```

The live Modrinth path still works with `ContentInstaller` now depending on `IContentProvider`
rather than `ModrinthClient` directly.

### V007.4 CurseForge blocked state is reported, not faked

Command: `Ferrite.Verify curseforge jei`

```
CurseForge: no API key configured.
  CurseForge needs an API key. Add one in Settings; see docs/HUMAN_ACTION_REQUIRED.md.
  secret store: <root>\config\accounts.bin
  live calls are BLOCKED EXTERNAL; see docs/HUMAN_ACTION_REQUIRED.md (H2).
```

No request is attempted without a key, so the harness reports the configuration state instead of
producing an error that could be mistaken for a broken integration. Live CurseForge calls remain
BLOCKED EXTERNAL until a key is supplied; the client, key storage, modpack installer, and
retail-file handling are covered by the tests in V007.1.

### V007.5 Interface: provider switch and key entry rendered

Command: `FERRITE_UI_SHOTS=<dir> dotnet run --project tests/Ferrite.App.Tests`

The real views were rendered headlessly and inspected as images:

- `browse-curseforge.png` — the browser switched to CurseForge shows `Search CurseForge` as the
  placeholder, keeps the Modrinth-style filter row, and explains the state in one line
  ("CurseForge needs an API key. Add one in Settings; ...") instead of an empty list or a dialog.
  The first render of this screen clipped that sentence at the right edge; the status text was
  moved out of the filter row onto its own wrapped line, and the duplicated status/provider line
  was collapsed to one. Both were fixed and re-rendered.
- `page-settings.png` — Settings gained a *Content providers* section: a masked key field, a
  *Remove key* action, a live status line ("No key stored. CurseForge browsing and modpack
  installs stay disabled." on a fresh profile), and an explanation that the key is stored outside
  the settings file.

The render pass also asserts the page text, so a regression that hides the explanation fails the
test rather than only looking wrong.

---

## V008 - Crash analysis, diagnostics bundle, and operation history (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5. The crash fixture is a real report that already existed
on this machine from a 1.8.8 session; it is read, never modified.

### V008.1 Parsing a real crash report

Command: `Ferrite.Verify crash`

```
Report: C:\Users\wwmky\AppData\Roaming\.minecraft\crash-reports\crash-2026-05-25_02.34.22-client.txt
  parsed: 13 frame(s), 0 listed mod(s)

Crash report
============
File:        crash-2026-05-25_02.34.22-client.txt
Time:        2026-05-25 02:34:00
Description: Initializing game
Cause:       java.lang.NullPointerException: Initializing game
Game version in report: 1.8.8
```

The report writes its timestamp in US short form (`5/25/26 2:34 AM`), carries no line numbers in
its frames (`at net.acx.a(acx.java)`), and repeats the trace under `-- Head --`. All of that is
handled: the timestamp parses, frames keep class and file without inventing a line number, and
identical frames collapse from 32 to 13. Fabric-style (`Fabric Mods:` inside the details block)
and NeoForge-style (`-- Mod List --`) reports are covered by `CrashReportTests`.

### V008.2 Attribution against 48 real mods, with no false positives

Command: `Ferrite.Verify crash <report> --game-dir <instance>/minecraft`

```
Using mods from instance Fabulously Optimized (imported)
Attributing against 48 installed mod(s)

Mods referenced by the stack trace
----------------------------------
No installed mod's classes appear in the stack trace.
(A frame is evidence that code ran, not proof of cause: treat this as a starting point, not a verdict.)
```

This is the negative case and it matters: the 1.8.8 report is obfuscated (`net.acx`, `net.a5q`),
and none of the 48 Fabric mods are blamed for it. The positive case is covered by
`CrashReportTests`, where a `net.caffeinemc.mods.sodium` frame is attributed to Sodium while the
`net.minecraft` and `java.base` frames are never attributed, and by the rendered Logs tab in
V008.5.

### V008.3 Support bundle

Command: `Ferrite.Verify diagnostics --out <path>`

```
Bundle: C:\Users\wwmky\AppData\Roaming\Ferrite\tmp\ferrite-diagnostics-20260921-012804.zip
  instance: Fabulously Optimized (imported)
  size:     1.7 KiB
  entries:  4
    system.txt
    notes.txt
    launcher/ferrite.log
    instance/instance.json
```

`system.txt` was read back out of the archive and contains the launcher version, runtime, OS and
architecture, data root, all six detected Java runtimes with vendor and architecture, and the
instance's version, loader, Java override, memory, and modpack identity. `DiagnosticsTests` covers
a populated bundle: a launcher log containing a registered secret, an instance log, a crash
report, and the generated `crash-analysis.txt`, asserting that no entry contains the secret and
that no entry name contains `..`.

### V008.4 Operation history

Every long-running action in the UI goes through `BeginActivity`/`EndActivity`, which now opens and
closes an operation scope, so the history is produced by the same code path the user drives rather
than by separate instrumentation that could drift. Entries record the label, outcome, start time,
and duration, are appended as one JSON object per line, and are bounded in memory and on disk.
`DiagnosticsTests` covers recording, newest-first ordering, the bound, persistence across a reload,
and a damaged line being skipped instead of breaking the history. One real defect was found here:
the file was written with the pretty-printing document options, so each entry spanned several
lines and nothing could be read back. The history now uses a compact options instance, with a test
asserting one entry per line.

### V008.5 Interface: crash analysis and diagnostics section

Command: `FERRITE_UI_SHOTS=<dir> dotnet run --project tests/Ferrite.App.Tests`

The real views were rendered headlessly and inspected as images:

- `instance-logs.png` — the Logs tab lists crash reports on the left and renders the analysis on
  the right: description, cause, the report's own suspects, the frames that were attributed, the
  mods listed but no longer installed, and the stack trace, with the caveat that a frame shows
  where code ran rather than proving cause.
- `settings-diagnostics.png` — Settings gained a *Diagnostics* section with the recent operation
  list (including its empty state), a notes field for the user's own description, and the bundle
  export action.
