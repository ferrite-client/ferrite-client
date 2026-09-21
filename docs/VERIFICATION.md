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

---

## V009 - Offline metadata cache (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, live Modrinth for the successful half of the scenario.

### V009.1 Live fetch, then the same query with the service unreachable

Command: `Ferrite.Verify cache sodium`

```
Live search: 351 hits, 5 returned
  cached: Sodium
  cache dir: <root>\data\cache\content
[info] CachedContentProvider: modrinth served modrinth-search-2102ef6d2d13f8a1 from cache
       (age 00:00:11.2804602) after No connection could be made because the target machine
       actively refused it. (127.0.0.1:1)
Offline search: 5 hit(s) from cache just now
  first hit: Sodium
```

The second call goes through a client whose endpoint cannot accept connections, and the real
Modrinth payload comes back from disk with its age. This is the whole feature: a search that would
otherwise produce an empty list produces usable results and a truthful label.

### V009.2 What does not fall back to the cache

`ContentCacheTests` covers the boundary deliberately:

- A 404 is a real answer and surfaces as `HttpException`; the cached success is not substituted.
- A different query is a different cache key, so it reports "could not be reached and nothing is
  cached" instead of serving an unrelated result set.
- A damaged cache file is ignored rather than throwing.
- Keys separate providers and arguments, and are stable for identical requests.

Only transport failures, timeouts, and 408/429/5xx fall back. Hiding a 404 or a rejected API key
behind stale data would tell the user something untrue about the world.

### V009.3 Interface: the offline state is visible

Command: `FERRITE_UI_SHOTS=<dir> dotnet run --project tests/Ferrite.App.Tests`

`browse-offline-cache.png` renders the real browser with the provider pointed at a closed port and
a pre-populated cache: the result list shows Sodium, and below the filter row it reads
"Modrinth is unreachable; showing results cached just now." The render pass asserts both the single
result and the note, so a regression that silently drops the explanation fails the test.

`AppServices` gained an optional Modrinth endpoint parameter so this state is reachable in a test
without touching the network stack, which is also useful for pointing Ferrite at a mirror.

---

## V010 - Content updates (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, live Modrinth. Scratch data roots throughout, so nothing
in the user's library changed.

### V010.1 Install an old version, then update it

The install was pinned to an older Sodium release (`RncWhTxD`, mc1.21-0.5.11) so that a real update
existed:

```
$ Ferrite.Verify content 1.21.1 --slug sodium --version RncWhTxD --loader fabric
Instance: verify-1.21.1-Fabric (Fabric vanilla)
Plan: 1 file(s)
Installed 1 file(s)
  Sodium [fabric] id=sodium version=0.5.11+mc1.21 enabled=True

manifest: mods/sodium-fabric-0.5.11+mc1.21.jar <- modrinth:AANobbMI@RncWhTxD
```

```
$ Ferrite.Verify updates --instance verify-1.21.1-Fabric --apply
Instance: verify-1.21.1-Fabric (1.21.1, Fabric -)
Tracked content: 1 file(s)
  mods/sodium-fabric-0.5.11+mc1.21.jar <- modrinth:AANobbMI@RncWhTxD
modrinth: 1 update(s), 0 up to date, 0 skipped
  update: mods/sodium-fabric-0.5.11+mc1.21.jar RncWhTxD -> mc1.21.1-0.8.13-fabric
Applied 1 update(s)
Tracked content now: 1 file(s)
  mods/sodium-fabric-0.8.13+mc1.21.1.jar @ SMxNOGZ6 (1.5 MiB)
```

The replaced file is gone, the new one is present with its real size, and the manifest now points at
the new version — which is why the second `updates` run reports no further changes.

### V010.2 A defect the live run caught: a NeoForge build offered to a vanilla instance

The first update run was against a **vanilla** instance (the harness creates one when no loader is
given) and it selected `mc1.21.1-0.8.13-neoforge`. That would have produced an install the game
cannot load. The cause was `ContentCompatibility`: with no loader on the instance, it skipped the
loader check entirely and took the newest version of any loader.

The rule is now explicit. A version whose loader list names a real mod loader is refused for an
instance that declares no loader, because Java-edition mod loaders are not interchangeable. Tokens
that mean "the base game" (`minecraft`, as Modrinth reports for every resource pack) do not count
as a loader restriction, so resource packs and datapacks still install into a vanilla instance.

Re-run on the same shape of instance:

```
$ Ferrite.Verify updates --instance verify-1.21.1 --apply
modrinth: 0 update(s), 0 up to date, 0 skipped
  warn: No compatible version of mods/sodium-fabric-0.5.11+mc1.21.jar exists for this instance.
No updates to install.
```

`ContentUpdateTests` covers both directions: a Fabric and a NeoForge build are rejected for a vanilla
instance, a Fabric build is accepted for a Fabric instance, and a resource pack declaring
`minecraft` is accepted by both.

### V010.3 What a failed update does

`ContentUpdateTests` also covers the cases that matter when the network or the provider misbehaves:

- A 500 during the replacement download leaves the installed file and the manifest entry exactly as
  they were, because the old file is only removed after the new one is verified.
- A version with no download URL (CurseForge third-party distribution disabled) is reported and not
  applied.
- A file that is no longer on disk, and a file recorded against a different provider, are reported
  as skipped rather than offered.
- A damaged manifest reads as empty instead of throwing.

### V010.4 Interface

`instance-updates.png` renders the Content tab: per-item checkbox, the file it will replace, the
version it moves to, the provider, and the check result. The Content tab previously duplicated the
Files tab's folder listings; it now has its own purpose, and the folder listings live only under
Files.

---

## V011 - Windows packaging (2026-09-21)

Environment: Windows 11 x64, .NET SDK 10.0.5. `FEATURE_PARITY.md` claimed `scripts/package.ps1`
existed; it did not. Rather than amend the claim, the script was written and both shapes were built
and run.

### V011.1 Both shapes publish

Command: `pwsh -File scripts/package.ps1`

```
Publishing framework-dependent (win-x64, Release)
Publishing self-contained (win-x64, Release)

framework-dependent      45 file(s)    30.7 MiB  archive 12.7 MiB
self-contained          232 file(s)   107.3 MiB  archive 47.8 MiB
```

The first packaging attempt produced a 130.8 MiB framework-dependent build. Almost all of it was
`libSkiaSharp.pdb` (84 MB) and `libHarfBuzzSharp.pdb` (21 MB) — native symbol files that Avalonia's
dependencies publish and that no user needs. SkiaSharp and HarfBuzzSharp cannot be recompiled here,
so the csproj drops those two entries from the publish output instead. Managed symbols are now
embedded in the assemblies (`DebugType=embedded`), so a stack trace from a user's diagnostics bundle
still carries line numbers without shipping loose PDBs.

### V011.2 The packaged executables actually start

Each published executable was launched with `FERRITE_HOME` pointing at an empty directory, left
running for twelve seconds, and inspected through its own log:

```
exitedEarly=False
Ferrite starting; version 0.1.0.0, data root ...\ferrite-packaged-46669465, secret protection os-backed
Settings loaded; theme Dark
Launcher ready; 0 instance(s), 0 account(s)
```

The same check passed for the self-contained build, which is the shape that has to work on a machine
with no .NET installed. Both stayed up for the full window and were then stopped, so this is a real
start of the packaged product rather than a successful `dotnet publish`.

Neither build is code-signed; that remains an external dependency (see `HUMAN_ACTION_REQUIRED.md`).

---

## V012 - Launcher self-update (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5. `docs/RESEARCH.md` said Ferrite implemented "check,
staging, verification, and hand-off". It did not; that sentence described an intention. It is true
now, and this is the evidence.

### V012.1 Publishing a signed feed

Command: `Ferrite.Verify sign-update --version 2.0.0 --package <build>.zip --out <feed> --kind framework-dependent`

```
Generated a new ECDSA P-256 key pair.
  public key:  <feed>\update-public-key.pem
  private key: written to the output directory; keep it offline, never commit it.
Package:  <build>.zip
  size:   12.7 MiB
  sha256: 6b74f09d14619125737397593922922c96f1bfc52d6258aa50e4349e9034bfec
Manifest:  <feed>\manifest.json
Signature: <feed>\manifest.json.sig
  signature verifies with the new key: True
```

The package was a real publish of this repository (`artifacts/framework-dependent`, 47 files,
12.7 MiB zipped), so the feed contains a build a user could actually run.

### V012.2 Checking it over real HTTP

Command: `Ferrite.Verify update-check --feed <feed> --key <feed>\update-public-key.pem --current 0.1.0 --stage`

```
Serving <feed> on http://127.0.0.1:50062/
Feed:    http://127.0.0.1:50062/
Current: Ferrite 0.1.0
Manifest verified. Newest release: 2.0.0 (2026-09-21 02:26:42Z)
Notes: Second release
Package: framework-dependent win-x64, 12.7 MiB
  [Downloading] 100.0%  Ferrite 2.0.0
Update 2.0.0 staged: 47 file(s) extracted to <root>\staging\2.0.0\payload
```

The feed files were served by a real (if minimal) HTTP server on the loopback interface; only a
loopback host may use plain HTTP, which is what makes a local feed testable without a certificate.

Then the manifest was altered after signing — its `version` changed from `2.0.0` to `9.9.9` — and
the same check was run again:

```
FAILED: UpdateException: The update manifest from http://127.0.0.1:43965/manifest.json did not
match its signature. Refusing to use it.
```

### V012.3 Applying the staged update

The generated hand-off script was run against an install directory containing the previous build
(`version.txt` = `0.1.0`, 46 files):

```
Waiting for Ferrite (pid 39068) to exit...
Installing Ferrite into <root>\install...
Starting <root>\install\whoami.exe
Ferrite update complete.

after: version.txt = 2.0.0
after: files = 47
staging exists = False
script exists  = False
```

The install was replaced with the staged payload (46 → 47 files, the new `version.txt`), the
staging directory and the script removed themselves, and a process was really started. The
stand-in executable in this run exists because the payload is a real launcher: starting the actual
Ferrite would have left a window running during an automated check. The production command starts
`Ferrite.exe`; the mechanism under test is identical.

`UpdateHandoffTests` runs the same script in the test suite, and also proves the guard: pointed at a
directory without Ferrite's staging marker, the script exits non-zero and copies nothing.

### V012.4 What is deliberately not accepted

`UpdateServiceTests` covers the refusals, because an updater that is too trusting is a code
execution path:

- A build with no embedded key refuses to check at all rather than accepting an unsigned feed.
- A manifest whose signature does not match is refused.
- A signature from a different key is refused, as are empty and malformed signatures.
- A feed that is not HTTPS is refused; HTTP is allowed only for a loopback host.
- Package URLs may not use `file:`, `ftp:`, an absolute path, or `../`, and a relative name resolves
  against the feed so a signed feed keeps working if it moves host.
- A package whose SHA-256 does not match is refused before anything is unpacked.
- A package without `Ferrite.exe` is refused, because it is not a build.
- A manifest from a newer schema, or one missing a runtime, url, or hash, is refused.

### V012.5 Interface

`settings-updates.png` renders the *Launcher updates* section: feed URL, the startup-check toggle,
the check and download actions, and the hand-off command once a build is staged. In a build with no
embedded key it states that plainly — "This build carries no update signing key, so an update feed
cannot be trusted" — rather than offering a check that cannot succeed.

---

## V013 - LAN world discovery (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, real multicast on the machine's LAN interface.

### V013.1 A broadcast is received on the address the game uses

Command: `Ferrite.Verify lan --seconds 6`

```
Listening on 224.0.2.60:4445 for 6s
Emulated a Minecraft broadcast: "Ferrite verification world" on port 51234
  LAN world: Ferrite verification world at 26.18.170.204:51234

1 LAN world(s) observed.
```

The listener joined the multicast group the game broadcasts to and reported the world with the
sender's local address and the announced port. The scenario emulates one broadcast because
verification cannot conjure a second Minecraft player, but everything after the datagram — the
receive loop, the parse, the sender address, the port, and the freshness bookkeeping — is the same
code path a real world uses. The address is in the local network control block, so a router does not
forward it: this only ever sees the local network.

### V013.2 What the parser accepts and refuses

`LanWorldDiscoveryTests` covers the payload the game sends, `[MOTD]name[/MOTD][AD]port[/AD]`:

- A complete payload parses into a name and a port; legacy section-sign colour codes are stripped.
- A world with an empty name still counts, because the port is the part that matters.
- A payload with a port but no name block is not a Minecraft announcement and is refused, as are a
  port of zero, a port above 65535, a non-numeric port, an empty datagram, and a datagram over 1 KiB.
- A 900-character name is truncated rather than stored.
- A repeated broadcast from the same world replaces the previous entry instead of stacking up, two
  worlds on one machine stay separate, junk datagrams are ignored without an exception, and a world
  that stops broadcasting disappears once its entry ages out.
- A real multicast datagram is delivered to the listener in-process, which is the same check V013.1
  performs through the harness.

### V013.3 Interface

`instance-servers-lan.png` renders the Servers tab: the saved-server list, a *Find LAN worlds*
action with the search status beside it, and each discovered world with its name, address, how long
ago it was announced, and an *Add* action that writes it into the instance's `servers.dat` with the
world's own name.

The listener is a shared socket, so leaving the detail page stops it; the detail page's Back action
does that rather than leaving a socket open for the rest of the session.

---

## V014 - Content-pack metadata (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, real client files and a real modpack instance in the
local Ferrite store.

### V014.1 Reading each version's own pack format

Command: `Ferrite.Verify packs`

```
  26.3: resource format 97.1, data format 121
  fabric-loader-0.19.3-1.21.1: resource format 34, data format 48
  fabric-loader-0.19.5-1.21.1: resource format 34, data format 48
  neoforge-21.1.251: resource format 34, data format 48
```

These come from the `pack_version` block of each version's own `version.json` inside the installed
client jar, which is why no version-to-format table is embedded in the source: the game states the
answer, and a table would drift the first time Mojang changes it.

### V014.2 A defect the first run caught

The first run reported `26.3: no client file, so no pack format is known` even though the jar was
present. The file uses a **newer** shape than older clients:

```json
"pack_version": { "resource_major": 97, "resource_minor": 1, "data_major": 121, "data_minor": 0 }
```

The parser only knew `resource`/`data` and a bare number. It now reads both shapes and carries the
minor version for display (`97.1`), comparing a pack's `pack_format` against the major, which is what
a pack's format field means. The message for a genuinely absent client file is now distinct from one
whose shape is not recognised.

### V014.3 Real packs evaluated against a real instance

```
Instance Fabulously Optimized (imported) (1.21.1)
Instance resource pack format: 34
  resourcepacks/Chat Reporting Helper.zip
      formats 18–64
      matches this version (format 34)
  resourcepacks/Mod Menu Helper.zip
      format 34
      matches this version (format 34)
  resourcepacks/SodiumTranslations.zip
      formats 15–64
      matches this version (format 34)
```

The three resource packs a real modpack ships are read from their own `pack.mcmeta` files and
compared with the format the instance's client file declares. Two declare ranges, one declares a
single format, and all three include it.

### V014.4 What the parser accepts and refuses

`PackMetadataTests` covers both `supported_formats` shapes (a list and a `min_inclusive`/
`max_inclusive` object), a single `pack_format`, pack filters, a chat-component description,
metadata inside a ZIP, an unpacked pack directory, a ZIP with no `pack.mcmeta`, and malformed input
(not JSON, no `pack` object, a non-object `pack`). Comparisons are covered for a match, a mismatch,
a range that contains the instance, and the case where the instance's format could not be
determined — which reports as unknown rather than as a mismatch, so the launcher does not claim a
pack is broken when it simply lacks the information.

### V014.5 Interface

`instance-files-packs.png` renders the Files tab with a pack that declares `format 15` against an
instance using format 34: the file name, its declared format, and "does not list format 34, which
this version uses". A file that is not a pack at all is still listed, without a compatibility claim.

---

## V015 - English and Polish interface (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, headless renders of the real views.

### V015.1 What was converted

Every user-visible literal in the application was moved into a table and read back by key:

- **Views:** 136 `Text`, `Content`, `Header`, and `PlaceholderText` attributes across seven views
  now use `DynamicResource`, so they re-render when the language changes. A scan for remaining
  capitalised literals in those attributes returns nothing.
- **View models:** the status, warning, and empty-state messages the launcher composes itself
  (`Settings saved`, `Installed N file(s) into X`, `Found N runtime(s)`, the activity strip, the LAN
  search states, verification and repair results, the update and pack summaries) go through the
  localizer with named placeholders.
- Switching the language in Settings applies immediately, without a restart, and is persisted with
  the rest of the settings.

### V015.2 Defects the tests caught

The test that checks every key against the views and the code found three real problems, all of
which would have shipped as blank or English text in the Polish interface:

- `Library` and `Browse` still appeared in the shell header, because the page title was bound to the
  `CurrentPage` enum. It now resolves through a key, so the header reads `Biblioteka`.
- `Ready` was a **field initializer** on the status text (`private string _statusText = "Ready";`)
  rather than a lookup, so the first status line was always English. The tests found it because the
  Polish render still contained `Ready`.
- Seven keys were defined but never used, and one code path looked keys up through a conditional
  expression the key scan could not see. The dead keys are gone and the conditional is two explicit
  lookups.

Two unused keys of my own invention (`L.Java.FieldDefault`, `L.Java.FieldArchitecture`) were also
removed: the Java page has no such labels, and inventing them would have been a table that describes
a design that does not exist.

### V015.3 What the tests enforce

`LocalizationTests` checks the properties that a translation actually needs:

- Both tables define exactly the same key set, with no empty value.
- Both languages use the same `{0}`-style placeholders for every key, so a format string cannot
  throw or drop a value in one language.
- More than 80% of keys differ between the languages, which catches a duplicated table.
- Every key used in a view or in code exists in both tables, and no key is defined without a use.
  This is the check that matters most: a missing key renders as nothing at all, with no error.
- An unknown key falls back to the key itself rather than throwing, and an unsupported language
  falls back to English.

### V015.4 Interface

`shell-polish.png` renders the real shell in Polish: `Biblioteka`, `Przeglądaj`, `Konta`,
`Ustawienia`, the header `Biblioteka`, the status `Gotowe`, the footer `KONTO` / `Brak konta`, and
the Library page with `Szukaj instancji`, `Importuj modpack`, `Nowa instancja`, and the Polish empty
state. The same render asserts that `Library` no longer appears anywhere.

### V015.5 Known limit

Text produced inside Core — provider errors such as "CurseForge needs an API key", verification
failures, HTTP messages — stays in English, and the parity matrix says so instead of claiming a
fully translated product. Those strings come from the engines that produce them and are often
embedded in exceptions; translating them would mean threading a language through every subsystem
for the smallest part of what a user reads.
