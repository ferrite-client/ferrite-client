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
