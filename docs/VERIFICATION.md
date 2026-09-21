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

---

## V016 - OptiFine (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, against real OptiFine files already on this machine.

### V016.1 Recognising real OptiFine files

Command: `Ferrite.Verify optifine`

```
  OptiFine HD_U_M6_pre2 for Minecraft 1.8.9
      version id: 1.8.9-OptiFine_HD_U_M6_pre2
      file:       ...\.minecraft\feather-mods\preview_OptiFine_1.8.9_HD_U_M6_pre2.temp.jar
  OptiFine HD_U_M6_pre2 for Minecraft 1.8.9
      version id: 1.8.9-OptiFine_HD_U_M6_pre2
      file:       ...\.minecraft\libraries\java\preview_OptiFine_1.8.9_HD_U_M6_pre2.jar

2 OptiFine installer(s) recognised.
```

Identification is OptiFine's own `Main-Class: optifine.InstallerFrame` in the JAR manifest, which is
what distinguishes the installer from a mod JAR carrying the same classes. The version comes from
OptiFine's artifact name, which is the only naming OptiFine publishes.

### V016.2 Two defects the real files exposed

Neither would have been found by a fixture written from the documentation:

- The files are named `preview_OptiFine_...`, and the parser assumed the name starts with
  `OptiFine`. It now locates the OptiFine token and reads the version from the tokens after it.
- One file carries the partial-download marker **before** the extension
  (`..._pre2.temp.jar`), which produced the version `HD_U_M6_pre2.temp`. Stripping the extension
  first is what caused it; the marker is now removed from the full name before the extension is
  stripped, and both files yield the same version id.

With those fixed, both real files resolve to `1.8.9-OptiFine_HD_U_M6_pre2`, which is exactly the
version directory name OptiFine's installer creates.

### V016.3 What was implemented

OptiFine publishes no API and no headless installer entry point: its installer is a window with an
Install button. Ferrite therefore does everything around that click:

1. **Recognise** the installer the user downloaded, and refuse a file that is not one, with the
   download site named in the message.
2. **Refuse a version mismatch**: an installer for Minecraft 1.8.9 cannot be adopted into a 1.21.1
   instance, because the patched version would not launch.
3. **Run the official installer** with the Java the target version needs, in a working directory
   the launcher owns (`tmp/optifine-<version>`), so OptiFine never writes into the instance or the
   game's own directory.
4. **Adopt the result** into the launcher's version store: the version document is copied, and its
   OptiFine library is copied with it, so the instance launches from the store.
5. **Set the instance's loader** to OptiFine with the adopted version id, which is what the launch
   pipeline then composes and verifies.

`OptiFineTests` covers each step: installer recognition by manifest and name, the `preview_` prefix,
the marker before the extension, refusal of a mod JAR of the same vintage, adoption copying the
document and library into the store, adoption reporting a missing library instead of claiming
success, a clear failure for a version that is not installed, and discovery of installed OptiFine
versions in a game directory.

### V016.4 Interface

`instance-detail.png` shows the new Loader section: the current loader, an *Install OptiFine from a
downloaded installer...* action, and the explanation of what happens — including that the user
completes OptiFine's own window. The message is there because a launcher that silently opens a
third-party installer window would be confusing; a launcher that claims to install OptiFine without
that window would be lying.

---

## V017 - Java provisioning, and two loader defects (2026-09-21)

Environment: Windows 11 x64, .NET 10.0.5, live Mojang and Forge services. Scratch data roots; the
loader runs share the launcher's artifact store so the game files are not downloaded twice.

### V017.1 Automatic Java provisioning never worked, and now does

Command: `Ferrite.Verify provision-java --minecraft 1.21.1 --component java-runtime-gamma`

First run:

```
Mojang publishes no runtime for windows.
```

Mojang's runtime catalog is keyed by **architecture**: `windows-x64`, `windows-x86`, `windows-arm64`,
`linux`, `linux-i386`, `mac-os`, `mac-os-arm64`. The provisioner asked for `windows`, so the lookup
could never match and every provisioning attempt failed before downloading anything. The method now
maps the host's OS and architecture onto the catalog's key, and returns null only for an
architecture Mojang does not publish for (Linux arm64), where the honest answer is "no runtime",
not a wrong one.

After the fix:

```
Mojang publishes 6 runtime component(s) for windows-x64: ...
Component java-runtime-gamma: 2 published build(s)
Provisioning java-runtime-gamma (17.0.15)...
Provisioned: Microsoft 17.0.15 X64
  executable: <root>\data\store\runtimes\java-runtime-gamma\bin\java.exe
  version:    17.0.15
  vendor:     Microsoft
  source:     ManagedRuntime
  exists:     True
  reused on a second call: True
```

The runtime manifest was fetched, every file downloaded, the build extracted, and the result probed
by running it — the version and vendor in the output come from the JVM itself, not from the
catalog. A second call reuses it instead of downloading again.

### V017.2 Quilt installs, and proves version inheritance

Command: `Ferrite.Verify quilt 1.21.1`

```
Instance install complete: 3973 files, 875.7 MiB
```

The store gained `quilt-loader-0.17.0-beta.1-1.21.1`, whose version document declares
`inheritsFrom: 1.21.1` and the Quilt `KnotClient` main class. Resolving and installing that version
is the version-inheritance path running for real: the vanilla client, libraries, and assets came
from the inherited document.

### V017.3 Forge failed, and the reason was a real defect

Command: `Ferrite.Verify forge 1.21.1`

First run:

```
Processor net.minecraftforge.installertools.ConsoleTool failed with 1: Error: Could not find or load
main class net.minecraftforge.installertools.ConsoleTool
```

The jar was present and did contain that class. Forge's install profile lists a processor's
**dependencies** in `classpath` and does not list the processor's own jar — Ferrite added those
dependencies and treated the list as complete, so the JVM was handed a classpath without the class
it had been asked to run. NeoForge's profile happens to include the processor jar, which is why the
shared path looked correct until it was run against Forge.

The classpath is now built by `ForgeProcessorRunner.BuildClasspath`, which always places the
processor jar first, skips entries that were not downloaded, and deduplicates. `LoaderInstallTests`
covers all three behaviours.

After the fix:

```
Instance install complete: 3996 files, 911.3 MiB
```

The seven processors ran, the binary patcher produced a 28 MB patched client, and the store now
holds `1.21.1-forge-52.1.16` with `mainClass: net.minecraftforge.bootstrap.ForgeBootstrap`, 49
libraries, and `inheritsFrom: 1.21.1`. One processor declares `sides: ["server"]` and is skipped for
a client install; its outputs are `win_args.txt`/`unix_args.txt`, and the client version document
references no `args.txt` at all, so nothing is missing.

### V017.4 What this says about the remaining `IMPLEMENTED` rows

Both defects were in rows marked `IMPLEMENTED` with evidence like "same path as Fabric" or "same path
as NeoForge". Those claims were plausible and wrong. The audit continues row by row: a row moves to
`VERIFIED` only when the workflow has actually been run, which is how these two were found.

---

## V018 - Credential storage, and the boundary of the auth rows (2026-09-21)

### V018.1 Secure credential storage is verified

Command: `pwsh -File scripts/test.ps1` (`ProviderCredentialStoreTests`)

The store is exercised against the real operating system facility rather than a stub:

- A key is written through `ProtectedSecretStore`, which on Windows calls
  `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`.
- The test then reads the file on disk and asserts the plaintext value does not appear in it, so the
  protection is observable, not assumed.
- A second store instance reads the value back, which is a real decrypt by the same user.
- Clearing a key removes it from disk as well as memory.
- Loading twice does not discard what was written since the first load, which is the property the
  account store and the provider key store share one file for.

### V018.2 Why the rest of the authentication chain is BLOCKED EXTERNAL

Every remaining authentication row needs the same two things, both of which only the user can
provide: an Azure public-client application id (`HUMAN_ACTION_REQUIRED.md` H1 step 1-7) and a
Minecraft-owned Microsoft account to sign in with. There is no way to exercise a device-code
sign-in, an Xbox Live token exchange, an entitlement check, a profile fetch, or a token refresh
without them, and forging one would prove nothing about whether the chain works.

What is already done, and what the evidence column records it as: the full chain is implemented
(device code, Xbox Live user token, XSTS with XErr mapping, `login_with_xbox`, entitlements,
profile, refresh with an expiry threshold, Yggdrasil-overridable endpoints), and each step is tested
against a scripted HTTP boundary with the request shapes the services document.

The exact verification to perform afterwards is written in H1: enter the client id in Settings, sign
in, and confirm the account appears, a launch uses it, and a second account can be added and
switched between. Until then those rows are `BLOCKED EXTERNAL` rather than `VERIFIED`, and the
parity matrix says which dependency is missing for each one.

---

## V019 - Network settings and the instance EULA toggle (2026-09-21)

**Environment.** Windows 11 x64; .NET SDK 10.0.201 (runtime 10.0.5). No live endpoint is contacted
by this entry: every network step runs against a real loopback TCP server started by the test.

**Why this entry exists.** The proxy, the mirror overrides, and the download concurrency were
persisted settings that nothing read, and the instance EULA checkbox stored a flag that wrote no
file. Each test below asserts the effect in the layer that does the work rather than the stored
value.

### V019.1 Mirror overrides reach the HTTP stack

Command: `pwsh -File scripts/test.ps1` (`NetworkSettingsTests`)

A loopback HTTP server stands in for the mirror. `HttpService` is constructed with a
`MirrorResolver` that points `launchermeta.mojang.com` at that server, then asked for
`https://launchermeta.mojang.com/manifest.json`.

- The body the caller receives is the stand-in server's, not Mojang's.
- The stand-in records exactly one request, at `/manifest.json`: the path survived the rewrite and
  the request did not leak to the real host.
- `A_download_uses_the_configured_mirror` repeats this through `DownloadEngine`: the file lands on
  disk with the mirror's bytes and the stand-in records one hit at the original CDN path.
- `An_unconfigured_host_and_a_bad_override_are_left_alone` and `Clearing_the_overrides_stops_rewriting`
  cover the two ways an override can be wrong: a host with no entry, and an entry whose value is not
  an absolute address. Both leave the original URL untouched.

### V019.2 The proxy setting reaches the HTTP stack

Command: same (`NetworkSettingsTests.A_proxy_setting_routes_requests_through_the_proxy`)

A single-connection stand-in proxy on loopback answers any request. After
`UpdateProxy("http://127.0.0.1:<port>")`, a request for `http://origin.invalid/metadata.json`
returns the proxy's body, and the proxy's recorded request line contains `origin.invalid`. The
origin host does not resolve, so a response can only have come through the proxy.

### V019.3 The concurrency setting is live and bounded

Command: same (`NetworkSettingsTests.The_concurrency_setting_bounds_simultaneous_transfers`)

An engine built with `MaxConcurrency = 8` reports 8; setting `0` clamps to 1 and `999` to 64; six
files then download with the limit set to 2 and the run reports six successes. The limit is read
when a batch starts, so a settings change applies to the next download rather than the next restart.

### V019.4 Saving settings in the UI pushes the values into the running services

Command: `pwsh -File scripts/test.ps1` (`InstanceSettingsTests`)

`SettingsViewModel.SaveCommand` is run against a real data root with concurrency 3, a loopback
proxy, and a mirror line typed as `host=url`. Afterwards: `Downloads.MaxConcurrency` is 3, the
resolver is no longer empty and rewrites the configured host, and a freshly constructed
`SettingsStore` reads all three values back from the settings document on disk. This is what proves
the settings screen is wired to the services rather than to a stored-only model.

### V019.5 The EULA toggle writes the file it promises

Command: same (`InstanceSettingsTests.Saving_an_instance_writes_the_eula_file_it_promised`,
`EulaFileTests`)

An instance is created on a real temp root, then saved through the instance view model with the
toggle on. `eula.txt` exists in the instance's game directory and records acceptance. Turning the
toggle off rewrites it as `eula=false`. `EulaFileTests` separately pin the file contract: the
comment line and `eula=true` content, case-insensitive parsing, a comment containing `eula=true`
not counting, directory creation for a fresh instance, and the safety rule that withdrawing
acceptance does not touch a file the user wrote by hand.

`InstanceLauncher` also applies the flag immediately before a launch, so a file deleted by hand
comes back and a withdrawn acceptance is undone.

**Limitations, stated precisely.**

- The mirror tests use plain HTTP for the stand-in, so they prove the rewrite, the preserved path,
  and the request routing. They do not prove a production mirror's TLS. An operator pointing a host
  at a mirror must use an address whose certificate is valid for that mirror; the launcher does not
  relax certificate validation for overrides.
- Proxy credentials embedded in the proxy URL were not exercised; the value is handed to
  `HttpClient` as the user entered it.
- The EULA evidence is the file the launcher writes and the flag it derives it from. A launch was
  not repeated for this entry, so the game's own read of that file is not covered here.

---

## V020 - Mod list: search, filtering, ordering, bulk actions, and reversible removal (2026-09-21)

**Environment.** Windows 11 x64; .NET SDK 10.0.201 (runtime 10.0.5). Everything runs against a real
temporary data root and real archive files; no network is involved.

**Why this entry exists.** The mod tab had one unfiltered list, no bulk actions, and a Remove button
that deleted the file outright. A pack with hundreds of files is unusable that way, and removing a
mod a user dropped in is their data, not ours.

### V020.1 The list is searched, filtered, and ordered

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.InstanceModTests`

Four real archives are written into an instance's `mods` folder: a Fabric jar, a Forge jar with
`META-INF/mods.toml`, a Fabric jar with a different declared dependency, and a plain zip that
declares nothing. After a scan:

- The loader filter offers exactly the loaders present (`all`, `fabric`, `forge`, `unknown`), so the
  choices come from the instance rather than a fixed table.
- `maps` narrows to the one mod named "Beta Maps"; `fabric` matches by loader; `fabric-api` matches
  the mod that declares it even though the string appears nowhere in its name or file name.
- A query that matches nothing reports "no match" while still knowing the instance has mods, which
  is the difference between a filtered-out list and an empty folder.
- Selecting the `forge` loader filter leaves the single Forge mod.
- Ordering by size puts the largest file first; ordering by name gives
  `Alpha Storage, Beta Maps, delta, Gamma Core`.

`ShellRenderingTests.Instance_mods_tab_renders_the_search_and_filter` then renders the real view with
two mods and the query `sodium`: the rendered tree contains the matching card, not the filtered-out
one, and shows the "Showing 1 of 2 mods" count.

### V020.2 Disable, re-enable, and removal that keeps the file

Command: same (`A_mod_can_be_disabled_re_enabled_and_removed`)

Two jars are handed to the same install path the file picker and the drop handler use, plus a
`notes.txt` that must be refused. The two jars land in the instance's `mods` folder and the text file
does not. Disabling the first renames it to `sodium.jar.disabled` on disk, the rescan reports it as
disabled with its metadata still readable, and enabling it restores the original name.

Removing it now moves the file into the launcher's backups folder instead of deleting it, and the
bytes in the backup are compared with the original source file. The removed file is gone from the
instance and present in backups, which is the property the old delete did not have.

### V020.3 Bulk actions act on the selection

Command: same (`Bulk_actions_apply_to_the_selected_mods_only`)

With three mods, "select all shown" reports "3 selected" and disabling acts on all three at once,
leaving three `.disabled` files on disk; the list is rebuilt by the single rescan that follows, so
nothing stays ticked under an action that already ran. Enabling acts the same way. With the query
`alpha` in place, "select all shown" takes the one visible mod and not the two it hid, and removing
the selection moves only that file to backups.

**Limitations, stated precisely.** The OS-level drag-and-drop gesture itself is not simulated in the
headless tests; the drop handler in `InstanceDetailView.axaml.cs` calls the same validated
`InstallModFilesAsync` path these tests exercise. Removing a mod moves it to
`backups/removed-content/<instance>/` rather than the Windows Recycle Bin, because the launcher keeps
its own recoverable copies instead of depending on shell behaviour.

---

## V021 - Java selection: compatibility, the launcher default, a per-instance pin, and a user path (2026-09-21)

**Environment.** Windows 11 x64; .NET SDK 10.0.201 (runtime 10.0.5). Six Java runtimes are installed:
Adoptium 25.0.3 and Microsoft 25.0.1 on PATH and in the Minecraft launcher's runtime store, Adoptium
21.0.10, two 17s, and Oracle 1.8.0.51. Data root `%APPDATA%\Ferrite` (installations reused from
V001/V002, so no re-download was needed).

**Harness.** `dotnet run --project tools/Ferrite.Verify -- instance-launch <version> ...`. This
scenario goes through the product's own `InstanceLauncher` - the code path behind the Play button -
rather than re-implementing the launch, and reports the image of the process that actually started.

### V021.1 The required Java version comes from the version document

Command: `Ferrite.Verify instance-launch 26.3 --java Microsoft`

Result: `Version 26.3 requires Java 25`. The requirement is read from the resolved version document
rather than assumed, which is what makes the compatibility judgement specific to the version being
installed.

### V021.2 A per-instance pin is the runtime the game runs on

Command: same as V021.1.

```
Automatic choice: Eclipse Adoptium 25.0.3 X64 (C:\Program Files\Eclipse Adoptium\jdk-25.0.3.9-hotspot\bin\java.exe)
Pinned choice:    Microsoft 25.0.1 X64 (...\java-runtime-epsilon\windows-x64\...\bin\java.exe)
  The pin differs from the automatic choice, so the run distinguishes them.
Pinned via:       instance
Process image: C:\Users\wwmky\AppData\Local\Packages\...\java-runtime-epsilon\windows-x64\...\bin\java.exe
The game's own log shows it reached the renderer (Setting user / LWJGL).
PASS: the game ran on the pinned runtime (Microsoft 25.0.1 X64).
```

The pin was deliberately the runtime the automatic choice would not have picked, so a pass rules out
"the pin was ignored and the best fit happened to be the same". The comparison is against the image
of the started process, and the game's own log confirms a real start.

### V021.3 A user-supplied path is probed, used, and honoured as the launcher default

Command: `Ferrite.Verify instance-launch 26.3 --default --java <temp>\ferrite-user-java\bin\java.exe`

A directory junction in the temp directory points at a JDK that the environment scan does not look
at, which is what a hand-added path looks like. The run then:

- probed that path the way the Java page does: `User path accepted: Microsoft 25.0.1 X64 (Java 25)`;
- found 7 runtimes with the path supplied and 6 without it, in the same run;
- pinned it as the launcher-wide **default** rather than on the instance
  (`Pinned via: launcher default`), while the automatic choice was still Adoptium 25.0.3;
- started the game, and reported the process image as the pinned path - through the junction, which
  Windows resolves back to the real directory:
  `PASS: the game ran on the pinned runtime (Microsoft 25.0.1 X64)`.

### V021.4 Precedence and the stored list

Commands: `pwsh -File scripts/test.ps1` (`JavaSelectionPrecedenceTests`, `JavaPageTests`)

`JavaSelectionPrecedenceTests` pins the order - instance choice, then launcher default, then best fit
- and covers a preference that is not installed (falls back) and an empty catalogue (selects
nothing). `JavaPageTests` drives the Java page: a path that is not a runtime is refused with a reason
and is not stored; a real runtime is probed, stored in settings, offered with the user-specified
source, and still present after the settings document is read back from disk; and a stored path that
has since been uninstalled is dropped from both the list and the file on the next scan.

**Interpretation.** Verifies E04, E05, E06, E07, and B12. E05/E06/E07 were `IMPLEMENTED` before this
entry with the launcher default and hand-added paths read by the Java page but never reaching a
launch; that gap was found and closed here, and the fix is what the two live runs above exercise.

**Limitations, stated precisely.** The pinned runs used the verification harness's placeholder
identity (no Microsoft account exists in this environment, `HUMAN_ACTION_REQUIRED.md` H1), so they
prove the runtime and launch pipeline, not authentication. The junction stand-in resolves to an
installed JDK; nothing here proves behaviour for a broken or hostile executable beyond the probe
refusing it.

---

## V022 - Instance content: packs, datapacks, screenshots, and world icons (2026-09-21)

**Environment.** Windows 11 x64; .NET SDK 10.0.201 (runtime 10.0.5). Everything runs against a real
temporary data root with real zip archives, a real folder pack, real PNGs from the image encoder, and
a real `level.dat` written with the launcher's own NBT writer.

**Why this entry exists.** The Files tab listed resource packs, shader packs, and screenshots as
read-only file names, there was no datapack surface at all, and the row claiming pack
enable/disable/delete was describing actions that did not exist. This entry is the evidence that the
actions now exist and the listing is complete.

### V022.1 Resource packs and shader packs

Commands: `InstanceContentTests.Packs_are_listed_with_their_formats_and_can_be_disabled_and_removed`
and the render test `ShellRenderingTests.Instance_files_tab_renders_packs_datapacks_and_screenshots`.

A stand-in client jar carrying `pack_version` is written into the version's store, so compatibility is
judged against a version rather than skipped. Against the instance's format 34:

- `matching.zip` (format 34) is listed as compatible; `outdated.zip` (format 15) is listed and marked
  as a mismatch with the format it declares, which is the case the game silently ignores;
- a resource pack that ships as a **folder** is a listed entry like any other;
- disabling the folder pack renames the directory to `folder-pack.disabled` and the rescan reports it
  as disabled; re-enabling restores the original name;
- removing `outdated.zip` takes it out of `resourcepacks` and leaves the bytes in
  `backups/removed-content/<instance>/`;
- the shader pack appears in its own list.

The render test then draws the real tab: the pack names, the shader pack, the screenshot, and the
section labels are all in the rendered tree, together with the Enable/Disable, Open folder, and
Remove actions on each entry.

### V022.2 Datapacks, and world icons

Command: `InstanceContentTests.Datapacks_are_listed_per_world_and_a_world_icon_is_decoded`

A world is built from a real `level.dat` (name, game type, last played, seed, version), with an
`icon.png` and a datapack zip declaring format 48.

- The world's `icon.png` decodes onto the card (`HasIconBitmap` and a non-zero decoded width);
- the datapacks are listed **under that world**, not as instance-wide content, and
  `HasWorldDatapacks` reports whether any world has one;
- disabling the datapack renames it inside the world, and removing it moves it to the launcher's
  backups and leaves the world with no datapacks.

### V022.3 Screenshot gallery

Command: `InstanceContentTests.Screenshots_are_listed_with_a_decoded_thumbnail`

A 640x360 PNG written by the image encoder is decoded to a bounded thumbnail (width is at most 320),
so a gallery does not hold full-resolution images in memory. A non-image file dropped into the
folder is still listed, with no thumbnail and the reason in its note, rather than breaking the
gallery.

**Limitations, stated precisely.**

- Resource pack **order** is not settable from the launcher. Which packs are active, and in what
  order, is stored by the game in `options.txt`; a launcher that rewrites that file while the game
  may be running is how pack selections get lost, so the row says what is actually provided:
  listing, enable/disable, and removal. The in-game list remains the place to order packs.
- Thumbnails are decoded on demand when the Files tab opens. A folder with thousands of screenshots
  would spend time decoding them; a lazier paging scheme is not implemented.

---

## V023 - The library list, and what a delete does to user data (2026-09-21)

### V023.1 Search, ordering, and per-instance size

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.LibraryListTests`

Three real instances are created on a temporary data root, each with a file of a different size in its
`mods` folder and a different `LastLaunchedAt`, so every order has a distinct expected answer:

- the default order is most recently played first;
- ordering by name and by size both produce the documented order, and the size order uses the real
  byte count the card reports (`SizeBytes >= 65536` for the largest instance), which is the value
  computed off the UI thread when the card loads;
- the search box matches a name, a Minecraft version, and a loader, and a query that matches nothing
  returns an empty list.

The order control itself is the drop-down rendered next to the search box in `LibraryView.axaml`.

### V023.2 Deleting an instance keeps it

Command: `dotnet run --project tests/Ferrite.Core.Tests -- -class Ferrite.Core.Tests.InstanceManagerTests`

An instance with `options.txt` and a world inside it is deleted through the same call the library card
makes. The instance directory is gone, the whole tree is present under
`backups/instance-<name>-<timestamp>-<id>/minecraft/`, and the instance is no longer in the library
list. Verified by reading the user's file back out of the backup.

**Interpretation.** Verifies B14, B15, and B16. B16 covers instances (moved to backups) and content
removed from an instance (mods, packs, and datapacks, all moved to
`backups/removed-content/<instance>/` per V020.2 and V022.1).

---

## V024 - Structured logging and settings persistence (2026-09-21)

Command: `dotnet run --project tests/Ferrite.Core.Tests -- -class Ferrite.Core.Tests.LauncherDiagnosticsTests`

Both of these are failure-path features, so they are verified by making them fail.

### V024.1 The log file

Real log lines are written through `FileLoggerProvider` into a real directory and then read back:

- each line parses as its own JSON object with `ts`, `level`, `category`, and `message`;
- a message containing a bearer token is written with the token replaced, checked against the raw
  bytes of the file rather than against the API's return value;
- with a 512-byte limit and 40 lines logged, the file rotates: `ferrite.log` is still the current
  file and a `.1` file exists, so a long session does not grow one unbounded file.

### V024.2 The settings document

- A half-written document (`{"schemaVersion": 1, "maxConcurrentDownloads": `) loads as defaults, and
  the damaged file is copied into `backups/settings-*.json` with its original content intact.
- A document from a **newer** schema is refused rather than mangled: defaults are used, the newer file
  is preserved in backups, and the file on disk is left exactly as the newer build wrote it.
- An **older** document is migrated forward: the schema version is set to the current one, an
  out-of-range download concurrency is corrected, an unrelated setting is preserved, and the migrated
  document is written back.

**Limitations, stated precisely.** These verify the file logger and the settings store as components.
A GUI session's own log file is the same provider wired by `AppLogging`, but this entry does not
inspect a log produced by an interactive session.

---

## V025 - Browsing content: facets, project details, artwork, and modpack dispatch (2026-09-21)

### V025.1 Facets come from the provider, and narrow the search

Command: `Ferrite.Verify browse --query sodium --category optimization --minecraft 1.21.1 --loader fabric`

```
Provider: modrinth (configured: True)
Facet category        126 value(s)  e.g. 128x, 16x, 256x, 32x, 48x, 512x+
Facet loader           29 value(s)  e.g. babric, bta-babric, bukkit, bungeecord, canvas, datapack
Facet game_version    915 value(s)  e.g. 26.3, 26.3-rc-3, 26.3-rc-2, 26.3-rc-1, 26.3-pre-3

Search: query='sodium' category=optimization version=1.21.1 loader=fabric
  total hits: 28
  - Sodium [fabric,neoforge,optimization,quilt] by jellysquid3 228,694,255 downloads
  - Sodium Extra [cursed,fabric,neoforge,optimization,quilt,utility] by FlashyReese 96,965,977 downloads
  ...
  every hit carries the 'optimization' category
```

The last line is a check, not a summary: the scenario inspects every returned hit and fails if one of
them lacks the category that was asked for. Verifies J01 and J02.

**Defect found and fixed.** The first run reported `Facet game_version 0 value(s)`. Modrinth names
that facet's entries `version` where every other facet uses `name`, so the reader was dropping all 915
of them. Fixed in `ModrinthClient.GetTagsAsync`, with `ModrinthClientTests` pinning both field shapes
and asserting the facet a filtered search sends.

### V025.2 Project details, gallery, and changelog

Command: same run as V025.1, continued into the first result.

```
Project: Sodium (sodium)
  licence:      LicenseRef-Polyform-Shield-1.0.0
  categories:   optimization
  loaders:      fabric, neoforge, quilt
  versions:     41 declared
  body:         5596 characters
  gallery:      6 image(s)
  source:       https://github.com/CaffeineMinecraft/sodium
  versions for the filters: 22
  newest: mc1.21.1-0.8.13-fabric (release), game 1.21.1, loaders fabric
  changelog: 647 characters
  file: sodium-fabric-0.8.13+mc1.21.1.jar (1574609 bytes, sha1 present)
  best for the instance: mc1.21.1-0.8.13-fabric (release)
```

The panel is exercised end to end in the app tests as well: `BrowseFacetTests` pins that a facet combo
drives the filter the query uses, that "any" is a real choice rather than an empty selection, that the
project panel reports what it has and what it lacks, and that the description is prepared for a text
box (heading markers, rules, and code fences dropped, prose and list items kept).

Artwork is verified against a real socket (`GalleryImageTests`): a PNG is fetched over HTTP and
decoded to a width of at most 480, bytes that are not an image are reported on the frame, and an
unreachable URL is reported rather than thrown.

Verifies J03 and J05.

### V025.3 A modpack result is downloaded, verified, and dispatched

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.BrowseModpackInstallTests`

The browser's own install path is driven with a real archive served over loopback:

- the archive is requested from the URL the provider published and, with a matching SHA-1, reaches the
  unpacker, whose "not a modpack archive" refusal is what the user sees, and no instance is created;
- with a SHA-1 that cannot match, the install stops at the download with
  `Failed to download …: Checksum mismatch …` and the unpacker is never reached.

**Defect found and fixed.** A failed download reported only `Failed to download <file>`; the reason
was carried in an inner exception the user never sees. `DownloadEngine` now appends the reason, so a
checksum mismatch is distinguishable from an unreachable host.

Verifies J10 for the dispatch, download, and verification half. The install-and-launch half of a real
modpack is V005.1 and V005.2, which installed Fabulously Optimized through the same
`MrpackInstaller.InstallAsync` this dispatch calls.

**Limitations, stated precisely.**

- The changelog and the project description are shown as text. Neither is rendered as markdown: the
  description has its heading markers, rules, and code fences removed so it reads cleanly, and the
  changelog is shown as the provider published it.
- CurseForge facets are implemented against its categories endpoint but cannot be exercised here: the
  provider needs a user-issued API key (`HUMAN_ACTION_REQUIRED.md` H2), which is the same blocker as
  K05.
- At most six gallery frames are fetched, and only when a project panel is opened.

---

## V026 - Instance creation, launch settings, the log tab, and large-pack scanning (2026-09-21)

### V026.1 The creation form writes the loader onto the instance

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.LibraryCreateTests`

The form's persistence step is driven with a version, `fabric`, and `0.19.5` selected: the record that
lands in the store carries the loader and its version, its launch version id is
`fabric-loader-0.19.5-1.21.1`, and a reload of the store agrees. A vanilla creation writes no loader
version and launches `26.3` directly. Verifies B02 for the persistence half; V002 is the live half
(Fabric installed and launched).

### V026.2 Memory and JVM arguments reach the command line

Command: `Ferrite.Verify instance-launch 26.3 --java Microsoft --seconds 50`

The instance is given `MemoryMb = 3072` and a custom `-Dferrite.verify.marker=1`, and launched through
the product's own `InstanceLauncher`. From the run:

```
Command (credentials redacted):
  "...\java-runtime-epsilon\bin\java.exe" ... -XX:+UseZGC -Xmx3072M
  -Dlog4j.configurationFile=... -Dferrite.verify.marker=1 net.minecraft.client.main.Main
  --username FerriteVerify ... --uuid ***redacted*** --accessToken ***redacted***
  --clientId ***redacted*** --xuid ***redacted*** --versionType release
  instance memory in command: True; custom JVM argument in command: True; launch token redacted: True
The game's own log shows it reached the renderer (Setting user / LWJGL).
```

The command itself is new evidence this entry adds: `InstanceLaunchResult.CommandPreview` carries the
redacted command, so a diagnostic log or report can show how the game was launched. Verifies E10, and
re-confirms F10's redaction on the product launch path rather than the harness's own builder.

### V026.3 The log tab reads a bounded tail

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.InstanceLogTests`

A 200,000-line, 6.8 MB `latest.log` is read: exactly the last 400 lines come back, starting at line
199,600 and ending at 199,999, and the first lines are absent. With two logs present, the one with the
newer write time is shown; with none, the tab says so. Verifies N01.

### V026.4 Rescanning a large pack no longer reopens every jar

Commands: `dotnet run --project tests/Ferrite.Core.Tests -- -class Ferrite.Core.Tests.ModScanCacheTests`

200 real mod archives are scanned twice. The first scan opens all 200 and records 200 misses and no
hits; the second returns the same list from the metadata cache with 200 hits and no misses, and is
faster than the first because it opens nothing. Replacing one jar re-reads exactly that jar (1 miss,
19 hits of 20), and disabling a mod is a different file path with its own entry, so the disabled
state is never served from the enabled entry.

**Why this matters.** The brief calls out "repeatedly parsing unchanged JAR metadata" as a hot path to
avoid; opening every jar on every visit to the mod tab was exactly that.

**Limitations, stated precisely.** The cache is keyed on size and last-write time, so a file replaced
with different bytes but an identical size and timestamp would be served from the cache. Filesystems
that rewrite a file in place without changing its size need the timestamp to move, which is how
ordinary writes behave. The scanner's off-thread behaviour (`ScanAsync` wraps `Scan` in `Task.Run`) is
by construction and is not separately measured here.

---

## V027 - CurseForge boundary, provider keys, hostile input, and pack import (2026-09-21)

### V027.1 The CurseForge client, and the retail-file rule

Commands: `CurseForgeClientTests`, then `CurseForgePackTests`.

The client is exercised against a real HTTP boundary with the request shapes and response payloads
the service documents: search (including `classId`, `modLoaderType`, `gameVersion`), version listing
with hashes and dependencies, project lookup by numeric id, **bulk file lookup** (two files resolved
in one `POST /v1/mods/files`, with the withheld file coming back without a URL), the id mappings, and
the configuration error when no key is set (which is reported before any request is made).

The retail rule is verified at both levels: the client reports no download URL for a withheld file and
a URL for a distributable one, and `CurseForgePackInstaller.PlanFiles` places only the available file
while reporting the other two - one withheld by the author, one the API no longer resolves - as
warnings naming the reason, with a skipped count.

**Interpretation.** Verifies K01 and K04. Every call above goes over HTTP to a scripted boundary, so
what is proven is the client's behaviour against the documented API. What is not proven is a live
call, which needs a key: that boundary is K05, and K03 and L02 are marked BLOCKED EXTERNAL for it.

### V027.2 A provider key entered in Settings

Command: `SettingsKeyTests`

A key typed into Settings and saved reaches the credential store, the provider reports itself
configured, the field is cleared so it does not sit in the UI, and the status line says the key is
stored protected. The plaintext appears in neither the settings document nor the credential file on
disk, and a second store reading the file decrypts the key - which is what a restart does. Clearing
removes it from the store and from the file. Verifies K02.

### V027.3 Importing a pack, by button or by dropping it

Command: `LibraryImportTests`

Both entry points call the same method, so the import path is the thing to check:

- a `.zip` with no pack index is refused by name ("not a modpack: it has neither modrinth.index.json
  nor manifest.json");
- a CurseForge-shaped archive is recognised and dispatched to the CurseForge installer, where it stops
  at the missing API key rather than at the archive being unsupported - which is what proves the
  dispatch, manifest reading, and loader mapping ran;
- a path that does not exist is reported rather than ignored.

The drop target is now wired on the library (`DragDrop.SetAllowDrop`, drag-over filtering by
extension, drop calling the same import), and the file picker offers both `*.mrpack` and `*.zip`
because the archive's root entry decides which installer runs. Verifies L07, and the dispatch half of
L02.

**Limitations, stated precisely.** The OS drag-and-drop gesture itself is not simulated in the
headless tests; the handler reads the payload defensively (a drag from any application can carry
anything) and calls the import path these tests exercise.

### V027.4 Hostile metadata and archives

Command: `HostileMetadataTests`

Ten cases, each one an input a launcher cannot trust:

| Input | Expected | Observed |
| --- | --- | --- |
| `../escaped.txt`, `../../escaped.txt`, `C:/Windows/Temp/...`, `/etc/...` as archive entries | nothing outside the destination | nothing written outside; the checkpointed paths do not exist afterwards |
| an archive that expands past `MaxTotalBytes` | typed refusal | `PathSafetyException: Archive exceeds the total expanded size limit.` |
| a mod descriptor nested past the JSON depth limit | the mod is listed, unreadable | loader `unknown`, no id, scan continues |
| a descriptor larger than the 4 MB read cap | not parsed | loader `unknown` |
| NBT nested past the depth limit | typed refusal | `NbtException: NBT nesting exceeds the 64 level limit.` |
| an NBT list claiming 2,147,483,647 entries | typed refusal | `NbtException: List length 2147483647 is out of range.` |
| a pack index that is not JSON | typed refusal | `ContentProviderException: modrinth.index.json is not valid JSON.` |

The archive-traversal and entry-limit behaviour is also pinned by `ArchiveExtractorTests`, and the
CurseForge manifest validation by `CurseForgePackTests`. Verifies P03.

---

## V028 - Accessibility: contrast, names, and visible focus (2026-09-21)

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.AccessibilityTests`

Three checks, each against the real artefacts rather than an intention: the palette file, the view
files, and a rendered frame.

### V028.1 Contrast

The test parses `Styles/Palette.axaml`, computes WCAG relative luminance for every token, and asserts
4.5:1 for each text tone on all three surfaces, in both themes, plus the accent button's own text
against its fill.

**Defects found and fixed.** Three tones failed AA, all of them the small 12px text the interface uses
for secondary detail:

| Token | Theme | Surface | Before | After |
| --- | --- | --- | --- | --- |
| `FerriteTextFaint` | Dark | raised card | 3.54:1 | 5.50:1 (`#6C7480` → `#8C95A2`) |
| `FerriteTextFaint` | Light | sunken | 3.58:1 | 4.64:1 (`#82888F` → `#616870`) |
| `FerriteSuccess` | Light | sunken | 4.05:1 | 4.87:1 (`#2F7D72` → `#2B6F66`) |
| `FerriteWarning` | Light | sunken | 4.06:1 | 4.86:1 (`#8A6D1F` → `#7C611B`) |

The dark theme's faint tone was the worst offender at 3.54:1, and every one of the faint values is
used for text a user is meant to read (versions, sizes, hints), not for decoration.

### V028.2 Names

Every view is parsed and every `Button`, `TextBox`, `ComboBox`, `NumericUpDown`, and `CheckBox` must
carry a name, a placeholder, a tooltip, button content, or a bound item source. The check found 12
controls with none of those — the memory and window-size steppers, the JVM and game argument boxes,
the per-item update checkbox, the rename and clone name fields, the download-concurrency stepper, and
the client-id field — and each now has an `AutomationProperties.Name` taken from its own localised
label. Two new label strings (`FieldWindowWidth`, `FieldWindowHeight`) and a `SelectItem` name were
added in both languages for the controls that had no label of their own.

### V028.3 Focus

The window is rendered, a Tab key is sent through the headless input stack, and the frame after the
keypress is compared byte-for-byte with the frame before it. The test fails if focusing a control
changes nothing on screen, which is what an invisible focus state looks like.

Buttons, tabs, and list items now draw an accent-coloured two-pixel border on `:focus-visible`, and
text fields, combo boxes, and steppers draw the accent border on any focus, because a caret alone does
not say where the next keystroke lands.

**Limitations, stated precisely.** These are the desktop accessibility properties this environment can
check mechanically. A screen-reader walkthrough, a full keyboard-only pass over every workflow, and OS
text-scaling behaviour were not performed, so `O11` is verified for contrast, naming, and focus rather
than for every assistive technology.

---

## V029 - Moving the launcher's data folder (2026-09-21)

Commands: `DataRootRelocationTests` then `SettingsDataFolderTests`.

The row claimed data-location control with `FERRITE_HOME` alone, which meant the only way to move the
data folder was to set an environment variable before starting. There is now a Settings section that
copies the data to a chosen folder, records it in `ferrite-root.txt` next to the executable, and asks
the user to restart - because a running launcher cannot swap the root every open handle points at.

### V029.1 What is refused

| Choice | Result |
| --- | --- |
| the folder already in use | "That is already the launcher's data folder." |
| a folder inside the current root | "The new folder cannot be inside the current one." |
| a folder containing the current root | "The new folder cannot contain the current one." |
| a relative path | "The folder path has to be absolute." |
| a non-empty folder that is not Ferrite's | "That folder is not empty and was not created by Ferrite." |
| a folder that already holds a Ferrite data set | refused rather than merged with the current one |

Every one of these is decided before a single byte is copied, and the Settings test asserts the user's
own folder is still untouched afterwards.

### V029.2 What a move does

The data is copied file by file through a temporary sibling and moved into place, so an interrupted
move cannot leave a half-written file looking complete; the source is left in place, so a failed
restart does not lose anything. A real move of a seeded data root (config, logs, an instance with a
mod jar) copies every file, leaves no `.part-*` files behind, and the marker makes
`AppPaths.CreateDefault` resolve to the new folder on the next start. Clearing the marker returns the
launcher to its default location, and `FERRITE_HOME` still overrides the marker, which is verified
separately.

Verifies O06.

**Limitations, stated precisely.** The move copies rather than relocating, so the old folder is left
behind for the user to delete once the launcher has started successfully from the new one; nothing
here deletes a user's data. Free-space checking is limited to reporting what would be copied, since a
reliable free-space query for an arbitrary path is not available without a platform call for it.

---

## V030 - The intermittent update-test failure (2026-09-21)

`UpdateServiceTests.A_feed_whose_manifest_was_altered_after_signing_is_refused` failed twice across
full-suite runs, and never once in twelve consecutive runs of its own class. The pattern - only under
the whole suite, only a test that asserts the reason a feed was refused - points at the test
infrastructure rather than the product: every test class here starts its own loopback HTTP server, and
with the entire suite connecting at once a request occasionally fails as a transport error, which
produces a different exception message than the assertion expects.

Twelve isolated runs of the class passed; two further full-suite runs with the core test project
capped at four parallel collections passed. That cap is the change: it keeps the run parallel without
making the machine compete with itself. No assertion was weakened - the test still requires the refusal
to name the signature.

---

## V031 - The modpack lifecycle: identity, and updating over an instance (2026-09-21)

### V031.1 Pack identity on the instance

Command: `Ferrite.Verify modpack --slug fabulously-optimized`

A pack can now be fetched by project slug, the way the browser does, rather than by a URL the caller
had to find first. The live run installed the current release:

```
Project: Fabulously Optimized (fabulously-optimized)
Version: 14.1.0 (release)
Pack: Fabulously Optimized 14.1.0 (format 1)
Dependencies: fabric-loader=0.19.5, minecraft=26.2
Declared files: 51
Instance: Fabulously Optimized (8bdf3004-755e-4722-9434-7d8b9df8a9c3)
  launch version: fabric-loader-0.19.5-26.2
  files:          51 downloaded, 0 skipped
  overrides:      50
  pack identity:  Modrinth 'Fabulously Optimized' 14.1.0
  record now:     minecraft 26.2, loader Fabric 0.19.5
  mod inventory:  49 mod(s)
```

The instance records the provider, the pack name, and the pack version, which is what makes it
recognisable later. Verifies L04.

### V031.2 Updating over an existing instance

Command: `Ferrite.Verify modpack --slug fabulously-optimized --update-id <instance id>`

The update points both pack installers at an instance that already exists. Before the run, two files
the pack does not contain were written into the instance: `config/my-own-settings.txt` and
`mods/zzz-user-added.jar`.

```
Updating instance: Fabulously Optimized (8bdf3004-755e-4722-9434-7d8b9df8a9c3)
  before: minecraft 26.2, loader Fabric 0.19.5, pack 'Fabulously Optimized' 14.1.0
  warn: Existing instance content was backed up to ...\backups\modpack-Fabulously Optimized-20260921-064333
Instance: Fabulously Optimized (8bdf3004-755e-4722-9434-7d8b9df8a9c3)
  files: 51 downloaded, overrides: 50, mod inventory: 49 mod(s)
  pack identity: Modrinth 'Fabulously Optimized' 14.1.0
```

Both of the user's files are in the backup the warning names, with their contents intact (`fov:95`
read back out of `config/my-own-settings.txt`), and the instance keeps its id, so nothing dangling
points at a replaced instance. Verifies L05.

### V031.3 A failed update leaves the instance alone

Command: `dotnet run --project tests/Ferrite.App.Tests -- -class Ferrite.App.Tests.InstancePackUpdateTests`

Applying an archive that is not a pack reports "not a modpack" and the stored record is exactly what it
was - still Minecraft 1.21.1 on Fabric, with no pack identity. A CurseForge manifest is recognised and
reaches its installer, where it stops at the missing key rather than swapping the instance's loader on
the way. A missing file is reported rather than ignored.

**Limitations, stated precisely.** An update re-applies the pack: the content it replaces is moved
into a backup whose path the user is told, rather than merged in place. A file the user edited that the
pack also ships comes back as the pack's version, with the user's copy in the backup. Merging would
need a per-file three-way comparison against the previous pack revision, which the installed-content
manifest does not record per file.

---

## V032 - Quick play: the arguments are generated, the client did not act (2026-09-21)

**This entry records partial verification and an open question. H11 is not marked VERIFIED.**

### V032.1 Two defects found and fixed in argument generation

Command: `dotnet run --project tests/Ferrite.Core.Tests -- -class Ferrite.Core.Tests.LaunchCommandBuilderTests`

1. **The singleplayer feature flag was hardcoded off.** The builder filled in the
   `quickPlaySingleplayer` placeholder but always reported `is_quick_play_singleplayer = false`, and a
   version document only passes arguments whose feature is enabled. The result was that
   `--quickPlaySingleplayer` could never appear on any version, whatever the instance recorded. The
   flag now follows the request and whether the instance has a world.
2. **A placeholder value containing a space was torn into separate arguments.** Arguments were
   expanded and *then* split on spaces, so `--quickPlaySingleplayer "My World"` became
   `--quickPlaySingleplayer My World` as two arguments. Templates are now split first and the
   placeholders filled in afterwards, so a value with spaces stays one argument - which is what the
   game expects. `LaunchCommandBuilderTests` covers both.

The live command confirms the fix end to end:

```
... --quickPlayPath ...\minecraft\quickPlay\quickPlayLog.json --quickPlaySingleplayer "New World"
  quickPlaySingleplayer in command: True
```

### V032.2 What the client did

Commands: `Ferrite.Verify instance-launch 1.20.4 --world "New World" --seconds 70`, then
`... --join "mc.hypixel.net:25565"`, then the same singleplayer run with the quick-play log file
pre-created.

A 1.20.4 world copied from `%APPDATA%\.minecraft\saves\New World` was placed in the instance, so the
world and the client version match. In all three runs the game started, reached the renderer, and
stopped on the title screen - and in the singleplayer runs:

- `logs/latest.log` contains no world-loading lines;
- the world's `level.dat` was never touched;
- the `quickPlay` folder the client names in `--quickPlayPath` was never created, and a pre-created
  `quickPlayLog.json` was left as it was.

A probe of the client jar confirms the arguments exist in the client
(`quickPlayPath`, `quickPlaySingleplayer`, `quickPlayMultiplayer`, `quickPlayRealms` in
`net/minecraft/client/main/Main.class`, plus `quickPlayWorld` and `quickPlayData` elsewhere), so the
client does parse them; what it does next is not satisfied by what this launcher passes.

**Open question for the next pass.** The client's own gate is not documented in this repository and
there is no way to consult the current official specification from this environment. The next step is
to read Mojang's launcher/quick-play behaviour from an authoritative source (or compare the argument
list against a launch from the official launcher) and then either satisfy it or record it as an
external limitation. Until that is done, H11 stays `IMPLEMENTED`: the arguments this launcher produces
are correct and tested, the game entering a world is not demonstrated.

### V032.3 The arguments match Mojang's own specification

The authoritative source for this is the version document itself, and it was checked rather than
recollected. `1.20.4.json` (as published by Mojang, fetched and cached by this launcher) declares:

```json
{ "rules": [ { "action": "allow", "features": { "has_quick_plays_support": true } } ],
  "value": [ "--quickPlayPath", "${quickPlayPath}" ] },
{ "rules": [ { "action": "allow", "features": { "is_quick_play_singleplayer": true } } ],
  "value": [ "--quickPlaySingleplayer", "${quickPlaySingleplayer}" ] },
{ "rules": [ { "action": "allow", "features": { "is_quick_play_multiplayer": true } } ],
  "value": [ "--quickPlayMultiplayer", "${quickPlayMultiplayer}" ] }
```

The live command produced for that same version contains exactly those three, in that shape, with the
world name as one argument. So the launcher's half is specification-correct; the client's half is the
part that did not happen, and the cause is outside what this repository can determine.

### V032.4 What was tried, in full

| Configuration | Result |
| --- | --- |
| singleplayer, absolute `--quickPlayPath`, 26.3 client, world from 1.20.4 | title screen, no quick-play file |
| singleplayer, absolute path, 1.20.4 client with its own 1.20.4 world | title screen, world untouched |
| singleplayer, with the quick-play log pre-created as `{}` | title screen, file left as it was |
| singleplayer, relative `--quickPlayPath` (`quickPlay\quickPlayLog.json`), no pre-existing file | title screen, no file created |
| multiplayer against `mc.hypixel.net:25565` (reachable, pinged in V006.3) | title screen, no connection attempt in the log |

The wiki also documents the arguments and states that `--quickPlayPath` is relative to the instance's
files; both forms behaved identically, so the launcher keeps the absolute path, which is unambiguous.
The exact user action that would settle this is written up as `HUMAN_ACTION_REQUIRED.md` H6.

---

## V033 - Switching an instance's mod loader version (2026-09-21)

Commands: `LoaderSwitchTests` (App), then the live runs below.

The row claimed "switch loader version safely" while the only way to change one was to create a new
instance. The instance settings page now lists the published loader versions for the instance's own
Minecraft version and can move the instance onto one of them.

### V033.1 The order is the whole point

`InstanceLoaderSwitcher` in Core does three things in this order, and nothing updates the instance
until all three have succeeded:

1. install the loader version into the store;
2. install the instance's own layout for that version - which is what puts the natives for the new
   version on disk;
3. only then set the instance's loader and loader version and save it.

The App tests pin that order: the injected step is called while the record still names the old version,
and a step that throws leaves the stored record exactly as it was (still `0.19.5`, with the failure
message on screen). Choosing the version the instance already runs is a no-op rather than a reinstall,
and a vanilla instance has nothing to choose.

**Defect found and fixed.** The first version of this switch only installed the loader into the store.
The live run then failed preflight with `The natives directory is missing. Run repair on this
instance.` - the instance had no natives for the version it had just been pointed at. Step 2 above is
the fix, and the switch now lives in Core so the page and the verification harness run one sequence
rather than two similar ones.

### V033.2 A real switch, and the loader the game reports

Commands: `Ferrite.Verify instance-launch 1.21.1 --instance verify-1.21.1 --loader-version 0.19.5`,
then the same with `--loader-version 0.19.3` (75-second window).

```
Switching loader version: Fabric 0.19.5 -> 0.19.3
[info] MinecraftInstaller: Installing fabric-loader-0.19.3-1.21.1: 3968 files, 920190296 bytes
Launch version after switch: fabric-loader-0.19.3-1.21.1
  -Djava.library.path=...\instances\2348aef1-...\natives\fabric-loader-0.19.3-1.21.1
  -cp ...\libraries\net\fabricmc\fabric-loader\0.19.3\fabric-loader-0.19.3.jar;...
PASS: the game ran on the pinned runtime (Eclipse Adoptium 21.0.10 X64).
```

The game's own log, from the instance that was just switched:

```
[09:26:33] [main/INFO]: Loading Minecraft 1.21.1 with Fabric Loader 0.19.3
```

That is the loader itself naming the version it booted with, from an instance that was running 0.19.5
before the switch. Verifies G09.

**Limitations, stated precisely.** The switch runs when the user asks for it and shows progress; it
does not stop a running game first, so switching while the instance is running leaves the running
process on the old version (the next launch uses the new one). Reinstalling or repairing the version an
instance already runs stays where it was - the instance's Repair button, verified in V001.5 - rather
than being folded into this control.

## V034 - Instance folders (2026-09-21)

Commands: `InstanceGroupTests` (Core), `LibraryGroupTests` (App), and the whole suite
(`328` Core tests, `70` App tests, all passing).

XMCL's feature catalogue includes "Instance Folder Groups - arrange many instances into folders so
different worlds and setups stay easy to scan". The first parity inventory did not have it. Ferrite
now stores an optional folder name on the instance record and filters the library by it.

### V034.1 The folder is metadata only

`InstanceRecord.Group` is a nullable trimmed string and `InstanceManager.SetGroupAsync` is the only
writer; `NormalizeGroup` treats a blank name as "no folder". The Core tests assert that an assignment
survives a reload, that a blank name clears it, that filing an instance leaves its game directory
byte-for-byte alone, and that a clone inherits its source's folder. Nothing under the instance
directory moves, so a folder can never cost a user their world or their mods.

### V034.2 The library filter only offers folders that exist

The App tests drive the real view model: the library's folder control is `All folders` followed by
the folders actually in use, ordered by name; choosing one narrows the list and composes with the
search box; moving an instance into a folder adds that folder to the control, and clearing it removes
the folder again rather than leaving a selection that shows nothing. `InstanceCardViewModel` exposes
the chip label ("Move to folder", or "Folder: <name>") that opens the prompt, so filing an instance is
available from the library itself and the chip does not widen the card's action row.

### V034.3 The interface

The library header gains a folder control beside the sort control, and each card shows a folder chip
under its pack line. The instance settings page gains a FOLDER field beside the default server, saved
by the same Save action that writes the rest of the instance's settings. The chip is a real button
(`Button.chip` in the style sheet), so it is keyboard focusable and carries the standard visible-focus
treatment instead of being hover-only text. Every new label exists in English and Polish, which the
localisation tests enforce by failing on a missing, unused, or placeholder-mismatched key.

**Limitations, stated precisely.** Folders are a flat, single-level grouping: an instance is in one
folder or none, and folders have no nesting, colour, or ordering of their own. That matches what the
library filter needs; the parity row does not claim anything more.

## V035 - Custom mod groups (2026-09-21)

Commands: `ModGroupTests` (Core), `InstanceModGroupTests` (App), and the whole suite
(`334` Core tests, `72` App tests, all passing).

XMCL's catalogue has "Custom Mod Groups - build your own groups and save the rules that organize a
complex instance". The first inventory did not list it. Ferrite now lets an instance carry named mod
groups and filters the mod list by one.

### V035.1 A group is a rule, not a stored list

A `ModGroup` is a name plus a substring; a mod belongs to it when its display name or its file name
contains that substring, case-insensitively. The Core tests pin the rule (display name, file name,
either, neither, and an empty rule matching nothing), the add-or-replace semantics of `Upsert`, the
refusal of a blank name or rule, `Remove` by name ignoring case, and a round trip through the
instance record. Because membership is derived, a group cannot go stale when the pack underneath it
changes - the same reason the instance's own list is a view over a scan rather than a cache.

### V035.2 The mod tab's filter and editor

The App tests drive the real view model. The group filter is built from the instance's own groups
behind an "all groups" entry; choosing one narrows the list, and it composes with the search box
rather than replacing it. The editor refuses a group with a blank name or rule with an on-screen
message instead of silently accepting something that would match every mod, saves an accepted group
to disk so a reload still has it, adds it to the filter, and removes both the group and its filter
entry when asked. "Remove" is disabled for the "all groups" entry, so the only way to delete a real
group is to select a real one.

### V035.3 The interface and its effect on the layout

The mods tab gains a fourth filter control (group) beside query, loader, and order, and a `Groups`
button in its header that opens the editor. Adding the control meant widening that row rather than
adding an action to the already-full bulk-action row, so the tab keeps its existing button budget.
Every new label exists in English and Polish; the localisation tests fail on a missing, unused, or
placeholder-mismatched key, and they pass.

**Limitations, stated precisely.** Groups match on substrings of the mod's name or file name only;
there is no expression language, no per-group ordering, and no cross-instance group sharing. A mod
can be in several groups at once, which is a property of the rule rather than a stored membership.

## V036 - Quick actions (2026-09-21)

Commands: `QuickActionTests` (App), and the whole suite (`334` Core tests, `75` App tests, all
passing).

XMCL's catalogue has "Quick Action - find instances and commands without leaving the page you are
working on. Launch or create an instance". The first inventory did not list it. Ferrite now has a
palette over the pages and the library.

### V036.1 The list is built from live state

The palette is rebuilt from the current library whenever it opens and whenever the query changes, so
it cannot offer an instance that was deleted after the palette was first opened. Page navigation and
"New instance" come first so they stay reachable while the query is being typed; each instance
contributes an "Open" action and, when its card is not already running, a "Launch" action that runs
the same command the library card does rather than a second launch path.

### V036.2 What the tests pin

The App tests open the palette, assert that the page commands and the per-instance commands are
present, and that typing a query narrows the list to matching titles (and reports an empty result
instead of an empty-looking palette). Filtering is over titles, so "tech" finds both "Open Tech
pack" and "Launch Tech pack". Choosing a result runs it and closes the palette: choosing a page
navigates to that page, and choosing "Open <instance>" goes to the library with that instance's
detail page open. Closing clears the query so the next open starts empty.

### V036.3 The interface

The shell header carries a `Quick actions` control (the low-emphasis chip class), and `Ctrl+K` opens
the same palette from anywhere. The palette is a single field over a scrollable result list; each
result is a real button with the item's title as its accessible name, so keyboard and screen-reader
use work, and the header hint does not describe the shortcut in visible text. `Escape` closes it.
Every new label exists in English and Polish; the localisation tests pass.

**Limitations, stated precisely.** The palette matches on substrings of command and instance titles;
it does not search mods, versions, or settings, and it does not rank results by recency.

## V037 - Instance assistant (2026-09-21)

Commands: `InstanceAdvisorTests` (Core), `InstanceAssistantTests` (App), and the whole suite
(`344` Core tests, `77` App tests, all passing). The scope decision is recorded as `DECISIONS.md`
D029.

XMCL's catalogue has a "Built-in AI Agent - let an agent inspect the instance virtual filesystem and
explain an actionable fix". The first inventory did not list it. Ferrite now has an instance
assistant that reads the instance's own evidence and explains what to do.

### V037.1 Deterministic, not a model

The agent names a model; a model needs a credential and a network service this environment cannot
supply, and shipping a hidden one would be worse than shipping none. The closest legitimate
alternative is a rule-based advisor over the same evidence an agent would read, and that is what is
implemented: the crash reports (via the existing frame-mod attribution), the log tail, the declared
mod dependencies, the managed-file check, Java availability, and the last preflight result. The
rendering says so plainly and states that it never calls an external service, so the user is never
misled about what produced the answer.

### V037.2 The rules the tests pin

The Core tests pin each rule: an absent Java runtime is blocking; a blocking preflight issue becomes
a finding whose action names repair; a stack frame that references an installed mod is reported as
evidence and the wording says a frame is not proof; a mod the crash report loaded but the instance no
longer has is blocking; a required dependency that is not installed and is not provided by the loader
is reported (while `fabric-api` and `fabricloader` are recognised as provided); the known log
signatures (out of memory, duplicate mods, an incompatible mod set, a missing mandatory dependency, a
failed mixin, a missing class or method) each become a finding carrying the log line as evidence;
disabled mods are context rather than a problem; and findings are ordered worst first. A healthy
input produces a single context finding rather than an empty list, so the panel is never blank.

### V037.3 The interface

The instance detail page gains an `Assistant` tab: one button that runs the check and a scrollable,
selectable report. Pressing it refreshes the log, the crash reports, and the mod list first, so the
answer describes the instance as it is now rather than as it was when the page opened. The App tests
drive that whole path against a real crash report and a real mod archive - the assistant names
Sodium from the failing frame - and confirm that pressing it on an instance with nothing installed
reports the missing files and recommends repair instead of throwing.

**Limitations, stated precisely.** The assistant reasons over Minecraft errors and mod metadata; it
does not inspect an arbitrary file tree, it does not read the game's own in-memory state, and its
conclusions are rules over evidence rather than a diagnosis. A model-backed variant would need an
externally supplied endpoint and key, the same shape of dependency as the Microsoft client id and
the CurseForge key.

## V038 - Saved projects, and the browser's saved view (2026-09-21)

Commands: `SavedProjectStoreTests` (Core), `BrowseSavedTests` (App), and the whole suite
(`350` Core tests, `81` App tests, all passing).

XMCL's catalogue has "Mod Collections - save projects you want to revisit and turn a collection into
a reusable setup. Followed Modrinth projects". The first inventory did not list it. Ferrite now lets
the browser save a project and lists the saved ones in a dedicated view.

### V038.1 The store is the only writer

`SavedProjectStore` owns `config/saved-projects.json` and serialises every change through one gate,
so two toggles cannot interleave into a half-written document. An entry is keyed by provider plus
project id, which is what makes "save, save again" a single entry and keeps the same project id from
two providers apart. The Core tests cover the toggle round trip, deduplication, the same id under two
providers, persistence across a fresh store over the same root, an unreadable document (the store
logs, preserves the file, starts empty, and stays usable), and a removal of something that was never
saved.

### V038.2 The saved view reuses the result list

The saved view is the same result list filled from the active provider's saved entries rather than
from a search, so selecting one still loads its versions, its project body, and its install path
through the code that already existed instead of a parallel path. Filtering to the active provider is
deliberate: a saved entry from a different provider would otherwise be resolved against the wrong
API. The App tests save three projects across two providers, open the saved view, and confirm only
the active provider's two appear; unsaving from the view removes the entry and the empty state says
so; saving and unsaving the selected project flips the button label and the stored list; and leaving
the view returns to searching.

### V038.3 The interface

The browser header gains a `Saved` control that swaps the result list for the saved view, with a
`Search results` control to come back, and the project panel gains a save toggle whose label reflects
the selected project's state. The saved view's empty state is its own message rather than the search
prompt. Every new label exists in English and Polish; the localisation tests pass.

**Limitations, stated precisely.** Saving is per provider, so the same project saved from two
providers is two entries; there is no folder or tag grouping inside the saved list, and the saved
view lists all of a provider's saved projects with no further filtering.

## V039 - Per-instance theme (2026-09-21)

Commands: `InstanceThemeTests` (Core), `InstanceThemeViewTests` (App), and the whole suite
(`366` Core tests, `86` App tests, all passing).

XMCL's catalogue has "Instance Theme - give each world its own identity with backgrounds, colours,
blur, fonts, and custom CSS". The first inventory did not list it. Ferrite now gives an instance a
background image and an accent colour.

### V039.1 What is implemented, and what is deliberately not

A background image and an accent colour are implemented. Blur, font choice, and custom CSS are not:
custom CSS and blur are properties of a web-rendered interface, and Ferrite is a native Avalonia
application with no stylesheet to inject - reproducing them would mean the one thing the product
brief forbids. The parity row claims only the two that exist.

### V039.2 The image is copied, and the colour is validated in Core

Picking a background copies the file into the instance's own `theme` folder rather than referencing
it where it was chosen, so moving or deleting the original cannot break the instance; a previously
chosen image with a different extension is removed so a stale file cannot be reintroduced. The
accent is validated by `InstanceTheme.TryNormalizeAccent` in Core, which accepts `#RGB` or
`#RRGGBB` with or without the hash and normalises to upper-case six digits. The Core tests pin the
normalisation, the blank-means-none rule, and five values that are not colours.

### V039.3 A typo does not erase an existing theme

The App tests drive the page. A valid accent is stored and produces a brush; an empty accent clears
it; and an invalid accent reports an error and stops the save *before* anything is written, so a
theme the user already had survives a slip in the box - which is the failure a naive "assign, then
validate" would have produced. Picking a background is asserted to copy the file, to be readable
after the source is deleted, and to survive a page reopen from the stored record; clearing it removes
the copy and the stored path.

### V039.4 The interface

The detail page shows a banner above the instance heading when a theme is set, using the background
image and the accent as its bottom border; the banner does not exist otherwise, so the default layout
is unchanged. The settings tab gains the accent field, a background picker, a clear control, and the
error line. Every new label exists in English and Polish; the localisation tests pass.

**Limitations, stated precisely.** The theme is a background image and an accent colour. There is no
blur, no font selection, and no custom stylesheet, for the reason given above.

## V040 - Local server: prepare, run, stop, export (2026-09-21)

Commands: `LocalServerTests` (Core), `InstanceLocalServerViewTests` (App), the live run below, and the
whole suite (`374` Core tests, `89` App tests, all passing).

XMCL's catalogue has "Launch a local server - turn an instance into a configurable local server with a
world, player limit, and exportable files" and "Server Export". The first inventory did not list
either. Ferrite now prepares, starts, stops, and exports a local server for an instance.

### V040.1 A real server, from this instance's own version

Command: `Ferrite.Verify local-server 1.21.1 --root <tmp> --seconds 240 --out <tmp>/server-export.zip`.

```
Version 1.21.1 publishes a server jar: True
  [Downloading] 100.0%  6.1 MiB/s    Minecraft server
Server jar: ...\server\server.jar (51,627,615 bytes)
Java: Eclipse Adoptium 21.0.10 X64 (C:\Program Files\Eclipse Adoptium\jdk-21.0.10.7-hotspot\bin\java.exe)
Command: "...\java.exe" -Xmx2048M -jar server.jar nogui
[info] LaunchService: Launched instance acc18a47-... (pid 20340) with 4 arguments
The server reported that it is ready.
The server created its world folder: True
The server was stopped.
  [10:34:50] [Server thread/INFO]: Preparing level "ferrite-verify"
  [10:34:54] [Server thread/INFO]: Preparing spawn area: 51%
  [10:34:54] [Server thread/INFO]: Done (4.174s)! For help, type "help"
Exported to ...\server-export.zip (103,501,377 bytes)
PASS: a real Minecraft server started from this instance's own version.
```

The jar is the version's own `downloads.server` artifact, fetched over TLS and verified against the
version document's SHA-1 before it is used. The readiness line is the server's own, not a timer, and
the world folder really appeared. The export was then opened: 48 entries including `server.jar`,
`server.properties`, `eula.txt`, and `ferrite-verify/level.dat`.

### V040.2 The settings file is merged, not rewritten

`ServerPropertiesDocument` edits the keys the launcher owns and leaves every other line - a comment, a
key the user added, `difficulty=hard` - exactly as it was, and a value containing a newline (an MOTD
can) is escaped so it cannot split the file. The Core tests pin the merge, the round trip, the
refusal of a jar whose SHA-1 does not match, the honest error when a version publishes no server
download, `eula=false` until the user accepts the agreement, the argument-list command, and the
export archive's contents.

### V040.3 The interface

The instance page gains a `Local server` tab: level name, MOTD, port, player limit, server memory,
online mode, and the EULA, with prepare, start, stop, and export. Preparing and starting both resolve
the instance's own version first, so a modded instance still gets the version its mods were built for.
The App tests confirm the tab shows the settings read from the server's own file rather than a second
copy, and that a never-prepared instance still opens with sensible defaults.

**Limitations, stated precisely.** The server is the vanilla one for the instance's Minecraft version;
the launcher does not yet build a loader-specific server (Fabric/NeoForge server jars), so a modded
instance's server is vanilla and will not load its mods. The server shares the instance's process slot,
so a client launch and a server launch for the same instance cannot run at once. Online mode defaults
to on; the verification run used `online-mode=false` deliberately so it could start without an
authentication service.

## V041 - Feed The Beast packs (2026-09-21)

Commands: `FtbClientTests` and `FtbPackInstallerTests` (Core), the two live runs below, and the whole
suite (`389` Core tests, `89` App tests, all passing).

XMCL's catalogue names FTB beside Modrinth and CurseForge as a place to find packs; the first
inventory did not include it. Ferrite now browses Feed The Beast and installs its packs. FTB needs no
key and no account, so nothing here is externally blocked.

### V041.1 Browsing, live

Command: `Ferrite.Verify ftb --term direwolf`.

```
Searching FTB for "direwolf"
Total: 19, returned 9
  [79] FTB Presents Direwolf20 1.16 - 368,158 installs
  [101] FTB Presents Direwolf20 1.19 - 249,882 installs
  [95] FTB Presents Direwolf20 1.18 - 235,129 installs
  [119] FTB Presents Direwolf20 1.20 - 232,297 installs
  ...
Pack 79: FTB Presents Direwolf20 1.16
  game versions: 1.16.4, 1.16.5
  loaders: forge
  authors: FTB Team
  versions: 23
    2111 1.13.1 (1.16.5, forge)
    2113 1.13.2 (1.16.5, forge)
```

FTB's search endpoint answers with pack ids only, so the client fetches the documents behind the ids
- bounded, and concurrently so the list is not built one request at a time. The Core tests pin that
those documents become real results, that a search for another project type asks the API nothing,
and that version filtering and the newest-match choice work.

### V041.2 A real pack install

Command: `Ferrite.Verify ftb --pack 101 --install --force`.

```
Version 1.13.1: 1025 file(s), 723 MiB
Installing as a new instance...
[info] FtbPackInstaller: FTB pack files: 1023 downloaded, 0 skipped
Instance: FTB Presents Direwolf20 1.19 1.13.1 (1.19.2-forge-43.3.7)
  1023 file(s) downloaded, 0 skipped, 0 warning(s)
  mods on disk: 296
PASS: an FTB pack was installed as an instance.
```

The instance's version is Forge 43.3.7 on Minecraft 1.19.2, which is what the pack declares rather
than a guess, and 296 mod jars are on disk. FTB publishes a file list rather than an archive, so the
installer resolves the version's targets, installs the loader and the game, then downloads each
declared file to the directory the pack names.

### V041.3 The paths come from a remote document

Because a pack's file paths are as untrusted as an archive entry, planning a file goes through the
same containment check as extraction. The Core tests pin that an optional file is not installed, a
file with no download is reported rather than skipped silently, a path that climbs out of the
instance is refused, an absolute path is refused, and every planned target stays inside the
instance. A defect the tests caught: the first version stripped `./` everywhere instead of only at
the front, which would have rewritten `../../../x` into a harmless path instead of refusing it.

### V041.4 The interface

FTB appears as a third provider in the browser, so search, facets, the project panel, and the version
list work through the same code as the other two providers. Its pack install goes through the FTB
installer rather than the archive path, since there is no archive to download.

**Limitations, stated precisely.** FTB publishes no category or loader vocabulary, so its facets offer
only "any" and filtering happens on the browser's own version and loader fields. FTB does not publish
per-version changelogs in the endpoint used here, so the changelog panel is empty for an FTB version.
Optional pack files are not installed; the pack's own optional list is not offered as a choice yet.

## V042 - LabyMod (2026-09-21)

Commands: `LabyModInstallerTests` (Core), the live run below, and the whole suite (`392` Core tests,
`89` App tests, all passing).

XMCL's catalogue lists LabyMod among the loaders it installs; the first inventory did not. Ferrite
installs LabyMod 4 and launches on it. LabyMod needs no key and no account, so nothing here is
externally blocked.

### V042.1 How LabyMod is installed, and why it is not an installer

LabyMod publishes a manifest (`api/v1/manifest/production/latest.json`), a library list
(`api/v1/libraries/production.json`), and one complete version document per Minecraft version. That is
metadata a launcher installs from, so nothing runs an installer window: Ferrite fetches the three
documents, adds LabyMod's libraries for that version and its client jar to the published version
document, writes the result into the version store as `<version>-LabyMod-4-<commit>`, and hands it to
the same installer every other version goes through. LabyMod's own assets are then fetched into the
instance's `labymod-neo/assets`, which is where LabyMod looks for them.

### V042.2 A real install and a real launch

Command: `Ferrite.Verify labymod 1.21.1 --launch --seconds 75 --instance verify-labymod-1.21.1`.

```
LabyMod 4.6.21 (commit 5842ece0)
Assets published: 6
[info] LabyModInstaller: Wrote the LabyMod profile 1.21.1-LabyMod-4-5842ece0: 131 library entry(ies)
[info] MinecraftInstaller: Installing 1.21.1-LabyMod-4-5842ece0: 4019 files, 966902548 bytes
[info] LabyModInstaller: Installed LabyMod 4.6.21 for instance ea742d04-...: 6 asset(s)
Installed version id: 1.21.1-LabyMod-4-5842ece0
Assets downloaded: 6
Profile written: True (...\store\versions\1.21.1-LabyMod-4-5842ece0\1.21.1-LabyMod-4-5842ece0.json)
Launching the instance on LabyMod...
  ...-cp ...\net\labymod\LabyMod\4.6.21\LabyMod-4.6.21.jar -Dnet.labymod.running-version=1.21.1
     net.minecraft.launchwrapper.Launch --username FerriteVerify ...
PASS: the game ran on the pinned runtime (Eclipse Adoptium 21.0.10 X64).
[info] LaunchService: Instance ea742d04-... exited with code 0 after 00:01:15.9838739
```

The classpath is the merged profile's: LabyMod's jar and its `net.laby`/`net.labymod` libraries beside
Minecraft's own, launched through `net.minecraft.launchwrapper.Launch` with LabyMod's own
`-Dnet.labymod.running-version` argument, and the game ran to exit code 0.

### V042.3 Two defects the live run found

**The library URL was treated as a repository base.** LabyMod's library URLs are the file's own
address, but Ferrite's loader profiles (Fabric, Quilt) use the field as a maven *repository*, so the
planner appended the maven path to the full URL and every library 404ed
(`.../asm-util-9.9.1.jar/org/ow2/asm/asm-util/9.9.1/asm-util-9.9.1.jar`). The fix puts the complete
address in an explicit artifact together with the maven path the file would occupy, which is what the
field means when it is not a repository. The Core test pins both the URL and the path.

**The manifest's size is stale.** The published `size` for the client jar is 14 bytes short of the
file it serves (`30,058,779` against `30,058,793`), so a size-checked download failed as a checksum
mismatch. The SHA-1 was checked against the live file and does match, so the hash is enforced and the
size is deliberately not passed; the code says so where the request is built.

### V042.4 The interface

The instance's settings tab gains an `Install LabyMod` control beside the OptiFine one. Unlike
OptiFine it needs no file from the user, so it is one button. Installing points the instance at the
new version and the library card shows `LabyMod 4.6.21`-style text through the existing loader label.

**Limitations, stated precisely.** LabyMod is installed for the instance's own Minecraft version, and
LabyMod publishes builds for a fixed list of versions - a version it does not publish is refused with
that reason rather than attempted. The six shared assets are fetched and stored but cannot be verified
against a checksum, because LabyMod publishes none. Bundled add-ons inside LabyMod are not managed
from Ferrite.

## V043 - Save preview and chunk tools (2026-09-21)

Commands: `WorldMapTests` (Core), `InstanceMapTests` (App), the live run below, and the whole suite
(`405` Core tests, `94` App tests, all passing).

XMCL's catalogue has "Save Preview and Chunk Tools - read a world visually before launching it and
make precise chunk-level changes. Select, copy, or delete Chunks". The first inventory did not list
it. Ferrite now reads a world's own region files, renders them, and edits chunks.

### V043.1 A real world, read and rendered

Command: `Ferrite.Verify world-map --world "<a real 1.20.4 world>" --delete 3`.

```
World: ...\instances\324e8c99-...\minecraft\saves\New World
Region files: 16
Map: 736x736 pixels, step 1, 2113 of 16384 possible chunk(s) present
Chunk range: x -22..23, z -22..23
Distinct blue values in the render (first 6): 126, 0, 109, 110, 111
```

That is a real world's Anvil files: 16 region files, 2113 chunks, and a 736x736 map whose shading
comes from each chunk's own `Heightmaps` (nine-bit heights packed seven per long, exactly as the
format stores them). A pixel with no chunk under it stays black, which is the honest reading of
"nothing is generated there".

### V043.2 Editing a copy, and what the backup holds

```
Copied the world to ...\world-map-copy so the original is untouched.
[info] WorldChunkEditor: Deleted 3 chunk(s) from ...\world-map-copy, rewriting 3 region file(s)
Deleted 3 chunk(s) from 3 region file(s): 2113 -> 2110 chunk(s) on disk
Region backup: ...\backups\world-chunks\world-map-copy-20260921-093529
Region files in the backup hold 482 chunk(s)
Copied 2 chunk(s) into a new world, offset by 32,32: 2 chunk(s) on disk at (54,55), (55,55)
PASS: a real world was read, rendered, and edited on a copy.
```

The count is exact in both directions: three chunks left the world, and the backup of the three
region files that were rewritten still holds all 482 of their chunks, so the deleted chunks are
recoverable. The copy landed at the source coordinates plus the offset, which means each copied
chunk's own `xPos`/`zPos` was rewritten - a chunk still naming its old position would have been
ignored by the game.

### V043.3 Three defects the tests and the live run found

**The data was written where the header said the next chunk was.** `Rewrite` computed the header
offsets from sector two but wrote the first chunk's bytes at offset zero, so every file it produced
was unreadable: the header pointed at data that was 8 KiB further along. The placeholder header is
written first now.

**The heightmap was read through the wrong accessor.** A heightmap is a `LongArray`, whose value is a
`long[]`; the reader was using the NBT-list accessor, which returns null for anything else, so every
chunk silently fell back to a flat height and the map came out uniform. The live run's "distinct blue
values" line is what made that visible.

**Stripping `./` everywhere instead of at the front** - a defect the FTB work found in the same
shape - is guarded here too: a chunk path that climbs out of the world is refused rather than
normalised.

### V043.4 The interface

The instance page gains a `Map` tab: a world picker, a copy-target picker, `Render the map`, and the
map itself. Clicking the map selects or deselects the chunk under the pointer - the image is drawn at
its own pixel size, so a pixel of the screen is a pixel of the map, and the selection is composited
into the bitmap rather than drawn as an overlay, so it stays aligned. Selected chunks are tinted
copper against the map's blues. `Delete selected chunks` and `Copy selected chunks` act on the
selection, and the status line reports the chunk count. Every new label exists in English and Polish.

**Limitations, stated precisely.** The map is a height-shaded chunk map, not the in-game renderer:
there is no block colouring, no biome colouring, and no lighting. Selection is per chunk, by
clicking; there is no drag-rectangle or brush yet. Copying places chunks at the same coordinates in
the target world (no offset control in the interface, though the editor takes one), and the game must
not be running on either world while chunks are edited.
