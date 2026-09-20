# Feature Parity Matrix

Authoritative completion matrix for Ferrite against the current stable XMCL
(research: `docs/RESEARCH.md`, section 1).

Statuses: `NOT STARTED` | `IN PROGRESS` | `IMPLEMENTED` | `VERIFIED` | `BLOCKED EXTERNAL`.

Rules for the status column:

- `IMPLEMENTED` means real, non-placeholder code exists and is covered by automated tests, but
  the capability has not been exercised end to end by a human or a scripted live run.
- `VERIFIED` means there is recorded, credible evidence that the real functionality works.
  Evidence lives in `docs/VERIFICATION.md` with a date, environment, steps, and result.
- `BLOCKED EXTERNAL` means completion depends on something outside this environment: a
  credential, an account, a certificate, or a third-party service. Each such row names the
  exact missing dependency and the exact user action.
- A row may never be marked `VERIFIED` merely because a class, screen, button, interface, or
  mock-only test exists.

`Evidence` points at the verification entry or the test that backs the status.

---

## A. Shell, foundations, and build

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| A01 | Native desktop app shell (no web UI) | Avalonia 12 desktop app `Ferrite.App`, single window shell | IN PROGRESS | build green 2026-09-20 |
| A02 | Original product identity and design system | "Ferrite" brand, graphite + copper design tokens, own icons | IN PROGRESS | - |
| A03 | Dark / light / system themes | `ThemeService` with three variants, live switch, persisted | NOT STARTED | - |
| A04 | Structured launcher logging | JSON-lines file logger with rotation and secret redaction | NOT STARTED | - |
| A05 | Crash-safe settings persistence | Atomic JSON write with schema version and migration | NOT STARTED | - |
| A06 | Storage layout separation | config / data / cache / instances / runtimes / logs / tmp / backups | NOT STARTED | - |
| A07 | Nullable-clean, warning-free build | `TreatWarningsAsErrors`, enforced in CI-style scripted build | IN PROGRESS | build green 2026-09-20 |
| A08 | Automated test suite | xUnit project `Ferrite.Core.Tests` | IN PROGRESS | - |
| A09 | Windows packaging | Framework-dependent + self-contained publish profile, zip artifact | NOT STARTED | - |
| A10 | Clean-checkout build script | `scripts/build.ps1`, `scripts/test.ps1`, `scripts/package.ps1` | NOT STARTED | - |

## B. Instances

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| B01 | Create vanilla instance | Instance creation flow writing metadata + isolated game dir | NOT STARTED | - |
| B02 | Create loader instance | Loader selection at creation time | NOT STARTED | - |
| B03 | Create modpack instance | From `.mrpack`, CurseForge zip, or remote version | NOT STARTED | - |
| B04 | Clone instance | Copy with isolation, no shared mutable state | NOT STARTED | - |
| B05 | Rename instance | Rename while preserving directory or renaming directory safely | NOT STARTED | - |
| B06 | Delete instance | Delete with confirmation, moves to trash or backs up first | NOT STARTED | - |
| B07 | Export instance | Export as `.mrpack` / CurseForge-format archive | NOT STARTED | - |
| B08 | Import instance | Import from exported archive | NOT STARTED | - |
| B09 | Archive instance | Zip instance without deleting | NOT STARTED | - |
| B10 | Open instance folder | Shell-open the instance directory | NOT STARTED | - |
| B11 | Instance metadata editing | Name, icon, memory, resolution, JVM args, game args, env vars | NOT STARTED | - |
| B12 | Per-instance Java selection | Bind a discovered or provisioned runtime to an instance | NOT STARTED | - |
| B13 | Per-instance EULA and advanced toggles | `eula.txt` creation, demo/quick-play toggles | NOT STARTED | - |
| B14 | Instance search, filter, sort | Library search across name/version/loader | NOT STARTED | - |
| B15 | Instance disk usage | Per-instance size calculation off the UI thread | NOT STARTED | - |
| B16 | Protect user data on destructive ops | Backup-before-delete policy for saves and configs | NOT STARTED | - |

## C. Minecraft versions and installation

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| C01 | Version manifest retrieval + cache | Cached `version_manifest_v2.json` with ETag/age policy | NOT STARTED | - |
| C02 | Version listing by channel | Release / snapshot / old_beta / old_alpha filtering | NOT STARTED | - |
| C03 | Version metadata resolution | Fetch + cache per-version JSON | NOT STARTED | - |
| C04 | Version inheritance | Recursive `inheritsFrom` merge (Fabric/Quilt/Forge) | NOT STARTED | - |
| C05 | Rule evaluation | OS, arch, feature rules with allow/disallow | NOT STARTED | - |
| C06 | Client JAR acquisition | `downloads.client` with SHA-1 verification | NOT STARTED | - |
| C07 | Library acquisition | Maven resolution with explicit `path` or derived path | NOT STARTED | - |
| C08 | Native extraction | Classifier resolution + `extract.exclude` handling | NOT STARTED | - |
| C09 | Asset index + assets | Objects store, SHA-1 verification, legacy virtual folder | NOT STARTED | - |
| C10 | Logging config download | `logging.client.file` + `-Dlog4j.configurationFile` | NOT STARTED | - |
| C11 | Integrity verification | Full SHA-1 verification of every managed artifact | NOT STARTED | - |
| C12 | Missing/corrupt detection | Scan and report missing or corrupt managed files | NOT STARTED | - |
| C13 | Repair | Re-download only broken artifacts | NOT STARTED | - |
| C14 | Shared artifact store | Global, content-addressed library/asset store reused by instances | NOT STARTED | - |
| C15 | Install progress | Aggregate + per-file progress, bytes, rate, ETA | NOT STARTED | - |
| C16 | Cancellation | Cancel any install/download safely with cleanup | NOT STARTED | - |
| C17 | `default-user-jvm` handling | Honour 26.x default user JVM args unless user overrode memory | NOT STARTED | - |

## D. Download engine

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| D01 | Bounded concurrency | Semaphore-bounded parallel transfers | NOT STARTED | - |
| D02 | Retry with backoff | Exponential backoff, retryable-status classification | NOT STARTED | - |
| D03 | Timeouts | Per-request and per-operation timeouts | NOT STARTED | - |
| D04 | Atomic finalization | Temp file + verify + atomic move into place | NOT STARTED | - |
| D05 | Hash verification | SHA-1 and SHA-512 verification modes | NOT STARTED | - |
| D06 | Duplicate request coalescing | Identical in-flight URL+target deduped | NOT STARTED | - |
| D07 | Resume | Range-based resume for interrupted large files | NOT STARTED | - |
| D08 | Mirror/fallback | Configurable fallback hosts (e.g. BMCLAPI-style) | NOT STARTED | - |
| D09 | Partial-failure cleanup | Temp files never masquerade as valid artifacts | NOT STARTED | - |
| D10 | Rate and ETA reporting | Live transfer rate and ETA aggregation | NOT STARTED | - |

## E. Java management

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| E01 | Java discovery | PATH, `JAVA_HOME`, registry, common vendors, launcher runtimes | NOT STARTED | - |
| E02 | Java version + arch detection | Parse `release` file and `java -version` output | NOT STARTED | - |
| E03 | Vendor detection | Temurin, Microsoft, Oracle, Zulu, GraalVM, etc. | NOT STARTED | - |
| E04 | Compatibility evaluation | Map Minecraft generations to required major versions | NOT STARTED | - |
| E05 | Global default Java | Persisted launcher-level default | NOT STARTED | - |
| E06 | Per-instance Java | Instance override | NOT STARTED | - |
| E07 | Custom Java path | User-specified executable with validation | NOT STARTED | - |
| E08 | Automatic Java provisioning | Download Mojang-published runtime for the version | NOT STARTED | - |
| E09 | Runtime validation | Launch `-version` and parse output before use | NOT STARTED | - |
| E10 | JVM/RAM editor | Memory presets, custom JVM args, quick presets | NOT STARTED | - |

## F. Accounts and authentication

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| F01 | Microsoft device-code sign-in | OAuth device flow, polling, cancel | NOT STARTED | - |
| F02 | Microsoft auth-code + PKCE sign-in | Browser-based flow with loopback listener | NOT STARTED | - |
| F03 | Xbox Live + XSTS chain | User token -> XSTS with XErr mapping | NOT STARTED | - |
| F04 | Minecraft services login | Token exchange + XUID capture | NOT STARTED | - |
| F05 | Entitlement check | `/entitlements/mcstore` ownership validation | NOT STARTED | - |
| F06 | Profile retrieval | Name, UUID, skins, capes | NOT STARTED | - |
| F07 | Token refresh | Automatic refresh with expiry handling | NOT STARTED | - |
| F08 | Multiple accounts + switching | Account list, active selection | NOT STARTED | - |
| F09 | Secure credential storage | OS-backed protection (DPAPI on Windows) | NOT STARTED | - |
| F10 | Secret redaction | Tokens never logged, diagnostics redacted | NOT STARTED | - |
| F11 | Yggdrasil-compatible servers | Custom auth server support (ely.by, littleskin, self-hosted) | NOT STARTED | - |
| F12 | Skin/cape preview | Render profile skin via a bounded fetch | NOT STARTED | - |
| F13 | Offline/cracked accounts | Deliberately NOT implemented (product brief forbids it) | BLOCKED EXTERNAL | product brief section 1 |
| F14 | Live Microsoft sign-in | Requires a real Microsoft account and Azure client ID | BLOCKED EXTERNAL | needs user action |

## G. Mod loaders

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| G01 | Fabric version discovery | `/v2/versions/loader/{game}` catalogue | NOT STARTED | - |
| G02 | Fabric profile install | Merge loader profile, resolve maven libraries | NOT STARTED | - |
| G03 | Quilt version discovery | `/v3/versions/loader` catalogue | NOT STARTED | - |
| G04 | Quilt profile install | Merge loader profile, resolve maven libraries | NOT STARTED | - |
| G05 | NeoForge version discovery | Maven metadata parsing per Minecraft version | NOT STARTED | - |
| G06 | NeoForge installer run | Official installer processors executed with argument list | NOT STARTED | - |
| G07 | Forge version discovery | Forge promotions + maven metadata | NOT STARTED | - |
| G08 | Forge installer run | Official installer processors executed with argument list | NOT STARTED | - |
| G09 | Loader update / reinstall / repair | Switch loader version safely on an existing instance | NOT STARTED | - |
| G10 | Loader launch behaviour | Loader-specific main class, args, and library ordering | NOT STARTED | - |
| G11 | OptiFine install | Official installer invocation | NOT STARTED | - |

## H. Launch pipeline

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| H01 | Classpath construction | Ordered libraries + client JAR, correct separators | NOT STARTED | - |
| H02 | Natives directory | Extracted natives directory per instance+version | NOT STARTED | - |
| H03 | Placeholder substitution | All `${...}` variables, fail-fast on unknown | NOT STARTED | - |
| H04 | Argument list process launch | `ProcessStartInfo.ArgumentList`, never shell strings | NOT STARTED | - |
| H05 | Game directory isolation | Per-instance working directory | NOT STARTED | - |
| H06 | Running instance tracking | Track live processes, exit codes, duration | NOT STARTED | - |
| H07 | Live log streaming | Stream stdout/stderr into the log view | NOT STARTED | - |
| H08 | Kill running instance | Requested and forced termination | NOT STARTED | - |
| H09 | Launch preflight | Java compatibility, files, account, memory validation | NOT STARTED | - |
| H10 | Command preview | Show the resolved command with secrets redacted | NOT STARTED | - |
| H11 | Quick play | `--quickPlaySingleplayer` / `--quickPlayMultiplayer` | NOT STARTED | - |
| H12 | Demo mode | `--demo` toggle | NOT STARTED | - |

## I. Content: mods, resource packs, shaders

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| I01 | Mod inventory scan | Off-UI-thread scan with metadata extraction | NOT STARTED | - |
| I02 | Mod metadata parsing | `fabric.mod.json`, `quilt.mod.json`, `mods.toml`, `neoforge.mods.toml`, legacy `mcmod.info` | NOT STARTED | - |
| I03 | Enable / disable | Reversible `.disabled` rename, no data loss | NOT STARTED | - |
| I04 | Remove mods | Delete with confirmation | NOT STARTED | - |
| I05 | Bulk operations | Multi-select enable/disable/delete | NOT STARTED | - |
| I06 | Local JAR install | Drag and drop or file picker, validation | NOT STARTED | - |
| I07 | Mod search/filter/sort | By name, loader, version, source | NOT STARTED | - |
| I08 | Dependency display | Show declared dependencies and incompatibilities | NOT STARTED | - |
| I09 | Update detection | Compare installed hash/version against providers | NOT STARTED | - |
| I10 | Resource pack management | List, enable/disable, delete, reorder | NOT STARTED | - |
| I11 | Shader pack management | List, enable/disable, delete | NOT STARTED | - |
| I12 | Datapack management | Per-world datapack listing | NOT STARTED | - |
| I13 | Screenshot gallery | Browse instance screenshots | NOT STARTED | - |
| I14 | Content-pack metadata parsing | `pack.mcmeta` compatibility ranges | NOT STARTED | - |

## J. Modrinth integration

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| J01 | Search with facets | Query, project type, categories, loader, version | NOT STARTED | - |
| J02 | Tag vocabularies | game_version, loader, category, project_type | NOT STARTED | - |
| J03 | Project details | Description, body, gallery, license, authors, links | NOT STARTED | - |
| J04 | Version listing | Per-project versions with filters | NOT STARTED | - |
| J05 | Changelog display | Markdown-rendered changelog | NOT STARTED | - |
| J06 | Dependency resolution | Required first, optional opt-in, recursive with cycle guard | NOT STARTED | - |
| J07 | Install into instance | Correct subfolder per project type | NOT STARTED | - |
| J08 | Update installed content | Match installed files to versions, offer updates | NOT STARTED | - |
| J09 | Compatibility guarantee | Never install a version incompatible with MC/loader | NOT STARTED | - |
| J10 | Modpack browsing | Search and install Modrinth modpacks | NOT STARTED | - |
| J11 | Offline/cached metadata | Serve cached results with clear offline state | NOT STARTED | - |

## K. CurseForge integration

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| K01 | API client | Search, project, files, dependency resolution | NOT STARTED | - |
| K02 | API key configuration | Secure per-user key storage, clear unset state | NOT STARTED | - |
| K03 | Modpack install | `manifest.json` + `overrides/` installer | NOT STARTED | - |
| K04 | Retail-file restriction handling | Report files that cannot be downloaded legitimately | NOT STARTED | - |
| K05 | Live CurseForge calls | Requires a user-issued API key | BLOCKED EXTERNAL | needs user action |

## L. Modpacks

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| L01 | Install `.mrpack` | Index + downloads + overrides, checksum verified | NOT STARTED | - |
| L02 | Install CurseForge zip | manifest.json + overrides + file resolution | NOT STARTED | - |
| L03 | Export `.mrpack` | Pack index with hashes and overrides | NOT STARTED | - |
| L04 | Modpack identity | Store project id, version id, provider in instance metadata | NOT STARTED | - |
| L05 | Update modpack | Apply new version, preserve user-added content | NOT STARTED | - |
| L06 | Overrides protection | Never clobber user-edited config without a backup | NOT STARTED | - |
| L07 | Drag-and-drop install | Drop a pack file onto the window to install | NOT STARTED | - |

## M. Worlds, servers, and multiplayer

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| M01 | NBT reader | Bounds-checked read-only NBT (gzip + raw) | NOT STARTED | - |
| M02 | World listing | `level.dat`: name, version, last played, gamemode, hardcore, seed | NOT STARTED | - |
| M03 | World icon extraction | Render `icon.png` thumbnails | NOT STARTED | - |
| M04 | World backup / restore | Zip a world, restore safely, verify | NOT STARTED | - |
| M05 | World delete / duplicate / export | Safe destructive operations with confirmation | NOT STARTED | - |
| M06 | `servers.dat` management | Read, edit, add, remove servers | NOT STARTED | - |
| M07 | Server status ping | Modern status protocol + legacy fallback, bounded | NOT STARTED | - |
| M08 | Server list UI | MOTD, player count, latency, version | NOT STARTED | - |
| M09 | LAN world discovery | Local discovery assistance | NOT STARTED | - |
| M10 | Internet LAN relay | Requires a relay service Ferrite does not operate | BLOCKED EXTERNAL | research section 9 |

## N. Diagnostics and observability

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| N01 | Instance log viewer | Tail `latest.log` and archived logs, bounded memory | NOT STARTED | - |
| N02 | Crash report parsing | Extract description, cause, stack, affected mods | NOT STARTED | - |
| N03 | Mod attribution from crash | Match stack frames to installed mods | NOT STARTED | - |
| N04 | Diagnostics bundle export | Redacted zip of logs, crash reports, system info | NOT STARTED | - |
| N05 | Installation diagnose + repair | Detect and fix missing/corrupt/mismatched files | NOT STARTED | - |
| N06 | Operation log | User-visible recent operations with outcomes | NOT STARTED | - |

## O. Launcher settings, storage, and updates

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| O01 | Settings screen | All persisted settings with validation | NOT STARTED | - |
| O02 | Download concurrency setting | Bounded, applied live | NOT STARTED | - |
| O03 | Mirror configuration | Per-endpoint mirror overrides | NOT STARTED | - |
| O04 | Proxy configuration | HTTP proxy settings applied to the HTTP stack | NOT STARTED | - |
| O05 | Cache management | Size reporting, safe cleanup, dedup reporting | NOT STARTED | - |
| O06 | Data location control | Move data root with validation | NOT STARTED | - |
| O07 | Update check | Signed manifest check over TLS | NOT STARTED | - |
| O08 | Update staging | Download, verify, prepare hand-off to an installer | NOT STARTED | - |
| O09 | Update feed | Requires externally hosted release infrastructure | BLOCKED EXTERNAL | research section 10 |
| O10 | Localisation | At least English and Polish, switchable | NOT STARTED | - |
| O11 | Accessibility | Keyboard navigation, focus visibility, labels, contrast | NOT STARTED | - |

## P. Cross-cutting quality

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| P01 | Archive safety | Zip-slip defence, absolute path and traversal rejection | NOT STARTED | - |
| P02 | Path safety | Canonicalisation, containment checks before writes | NOT STARTED | - |
| P03 | Malicious metadata defence | Size caps, depth caps, encoding validation | NOT STARTED | - |
| P04 | Network resilience | Offline states, retries, timeouts, graceful degradation | NOT STARTED | - |
| P05 | Performance with large packs | Caching, incremental scans, no UI-thread blocking | NOT STARTED | - |
| P06 | Visual QA pass | Full screen/state sweep with fixes | NOT STARTED | - |
| P07 | Security audit | Adversarial review of every untrusted path | NOT STARTED | - |
| P08 | Repository hygiene | `.gitignore`, no secrets, no build output committed | IN PROGRESS | - |

---

## Summary

| Status | Count |
| --- | --- |
| NOT STARTED | 0 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 0 |
| VERIFIED | 0 |
| BLOCKED EXTERNAL | 0 |

_Summary counts are regenerated with `scripts/parity-summary.ps1` after each milestone._
