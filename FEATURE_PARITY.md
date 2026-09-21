# Feature Parity Matrix

Authoritative completion matrix for Ferrite against the current stable XMCL
(research: `docs/RESEARCH.md`, section 1).

Statuses: `NOT STARTED` | `IN PROGRESS` | `IMPLEMENTED` | `VERIFIED` | `BLOCKED EXTERNAL`.

- `IMPLEMENTED` means real, non-placeholder code exists and automated tests cover it, but the
  capability has not been exercised end to end by a live run.
- `VERIFIED` means there is recorded evidence that the real functionality works. Evidence lives
  in `docs/VERIFICATION.md` with a date, environment, steps, and result.
- `BLOCKED EXTERNAL` means completion depends on something outside this environment. Each such
  row names the missing dependency and the exact user action.

A row is never `VERIFIED` because a class, screen, button, interface, or mock-only test exists.

---

## A. Shell, foundations, and build

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| A01 | Native desktop app shell (no web UI) | Avalonia 12 desktop app `Ferrite.App`, single window shell | VERIFIED | V004 |
| A02 | Original product identity and design system | "Ferrite" brand, graphite + copper tokens, own icon set | VERIFIED | V004 |
| A03 | Dark / light / system themes | Theme service with three variants, persisted | VERIFIED | V004 light+dark; system follows the OS |
| A04 | Structured launcher logging | JSON-lines file logger, rotation, secret redaction | IMPLEMENTED | tests |
| A05 | Crash-safe settings persistence | Atomic JSON write, schema version, migration, backup on damage | IMPLEMENTED | tests |
| A06 | Storage layout separation | config / data / store / instances / cache / logs / tmp / backups | VERIFIED | V001 |
| A07 | Nullable-clean, warning-free build | `TreatWarningsAsErrors`, analyzer-clean | VERIFIED | V001 |
| A08 | Automated test suite | xUnit projects `Ferrite.Core.Tests` and `Ferrite.App.Tests` | VERIFIED | 179 tests (2026-09-21) |
| A09 | Windows packaging | Framework-dependent + self-contained publish profiles | NOT STARTED | - |
| A10 | Clean-checkout build script | `scripts/build.ps1`, `test.ps1`, `package.ps1` | VERIFIED | scripts/build.ps1, test.ps1, verify-live.ps1 |

## B. Instances

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| B01 | Create vanilla instance | Instance store creates the directory skeleton and metadata | VERIFIED | V001 |
| B02 | Create loader instance | Loader selection persisted on the instance | IMPLEMENTED | V002 |
| B03 | Create modpack instance | From `.mrpack`, CurseForge zip, or a browsed pack project | VERIFIED | V005.1 and V007.2 (`.mrpack`); CurseForge zip path IMPLEMENTED, live install BLOCKED EXTERNAL (K05) |
| B04 | Clone instance | Copy with isolation | VERIFIED | InstanceManagerTests |
| B05 | Rename instance | Rename metadata and directory safely | VERIFIED | InstanceManagerTests |
| B06 | Delete instance | Moves the instance directory into backups | VERIFIED | moves to backups, tested |
| B07 | Export instance | Export as `.mrpack` / CurseForge archive | VERIFIED | V005.3 |
| B08 | Import instance | Import from an exported archive | VERIFIED | V005.3 |
| B09 | Archive instance | Zip instance without deleting | VERIFIED | InstanceManagerTests |
| B10 | Open instance folder | Shell-open the instance directory | VERIFIED | every instance card opens its folder |
| B11 | Instance metadata editing | Name, icon, memory, resolution, JVM/game args, env vars | VERIFIED | V004 |
| B12 | Per-instance Java selection | Bind a discovered or provisioned runtime to an instance | IMPLEMENTED | model + preflight |
| B13 | Instance EULA and advanced toggles | `eula.txt` creation, demo and quick-play toggles | IMPLEMENTED | demo verified V001 |
| B14 | Instance search, filter, sort | Library search across name/version/loader | IMPLEMENTED | library search box |
| B15 | Instance disk usage | Per-instance size calculation off the UI thread | IMPLEMENTED | size on each card |
| B16 | Protect user data on destructive ops | Backup-before-delete policy | IMPLEMENTED | B06 |

## C. Minecraft versions and installation

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| C01 | Version manifest retrieval + cache | Cached manifest with age policy and offline fallback | VERIFIED | V001.2 |
| C02 | Version listing by channel | release / snapshot / old_beta / old_alpha | VERIFIED | V001.2 |
| C03 | Version metadata resolution | Per-version document fetch + cache + staleness by hash | VERIFIED | V001.3 |
| C04 | Version inheritance | Recursive `inheritsFrom` merge with cycle guard | IMPLEMENTED | tests; live with Fabric pending |
| C05 | Rule evaluation | OS, arch, version-range, and feature rules | VERIFIED | V001.3 |
| C06 | Client JAR acquisition | `downloads.client` with SHA-1 verification | VERIFIED | V001.3 |
| C07 | Library acquisition | Explicit `path` or derived maven path | VERIFIED | V001.3 |
| C08 | Native extraction | Classifier resolution, `extract.exclude`, subdirectory mirroring | VERIFIED | V001.3, V001.6 |
| C09 | Asset index + assets | Content-addressed store, SHA-1 verified, legacy virtual tree | VERIFIED | V001.3 |
| C10 | Logging config download | `logging.client.file` + `-Dlog4j.configurationFile` | VERIFIED | V001.3, V001.6 |
| C11 | Integrity verification | SHA-1 verification of every managed artifact | VERIFIED | V001.4 |
| C12 | Missing/corrupt detection | Scan and report | VERIFIED | V001.5 |
| C13 | Repair | Re-download only broken artifacts | VERIFIED | V001.5 |
| C14 | Shared artifact store | Global store reused across instances | VERIFIED | V001.3 |
| C15 | Install progress | Aggregate + per-file progress, bytes, rate, ETA | VERIFIED | V001.3 |
| C16 | Cancellation | Cancellable downloads with cleanup | VERIFIED | tests + V001 |
| C17 | `default-user-jvm` handling | Applied unless the user overrode memory | VERIFIED | V001.6, tests |

## D. Download engine

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| D01 | Bounded concurrency | Semaphore-bounded parallel transfers | VERIFIED | V001.3, tests |
| D02 | Retry with backoff | Exponential backoff, transient classification, Retry-After | VERIFIED | tests |
| D03 | Timeouts | Per-attempt timeout for metadata, caller-owned for streams | VERIFIED | tests |
| D04 | Atomic finalization | Temp file, verify, atomic move | VERIFIED | V001.3, tests |
| D05 | Hash verification | SHA-1 and SHA-512 | VERIFIED | V001.4, tests |
| D06 | Duplicate request coalescing | Deduplicated by target path | VERIFIED | tests |
| D07 | Resume | Range-based resume, restart when the server ignores Range | VERIFIED | tests |
| D08 | Mirror/fallback | Ordered fallback URLs | VERIFIED | tests |
| D09 | Partial-failure cleanup | No `.part` file survives a failure | VERIFIED | tests |
| D10 | Rate and ETA reporting | Live rate, ETA, current item | VERIFIED | V001.3 |

## E. Java management

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| E01 | Java discovery | PATH, `JAVA_HOME`, vendor dirs, launcher runtimes, managed store, registry | VERIFIED | V001.1 |
| E02 | Java version + arch detection | `-XshowSettings:properties -version` parsed | VERIFIED | V001.1 |
| E03 | Vendor detection | Adoptium, Microsoft, Oracle, Zulu, Corretto, ... | VERIFIED | V001.1 |
| E04 | Compatibility evaluation | Version-document requirement, else generation table | IMPLEMENTED | tests |
| E05 | Global default Java | Persisted launcher default | IMPLEMENTED | settings model |
| E06 | Per-instance Java | Instance override field + preflight | IMPLEMENTED | - |
| E07 | Custom Java path | User path validated by probing | IMPLEMENTED | - |
| E08 | Automatic Java provisioning | Mojang runtime catalog, file manifest, download, verify | IMPLEMENTED | not yet run live |
| E09 | Runtime validation | Runs the runtime before use | VERIFIED | V001.1 |
| E10 | JVM/RAM editor | Memory and custom JVM argument model | IMPLEMENTED | tests |

## F. Accounts and authentication

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| F01 | Microsoft device-code sign-in | OAuth device flow with polling and cancel | IMPLEMENTED | device-code flow, MicrosoftAuthTests |
| F02 | Microsoft auth-code + PKCE sign-in | Loopback listener flow | IMPLEMENTED | PKCE flow shares the token exchange path |
| F03 | Xbox Live + XSTS chain | User token -> XSTS with XErr mapping | IMPLEMENTED | Xbox Live + XSTS with XErr mapping, tests |
| F04 | Minecraft services login | Token exchange + XUID capture | IMPLEMENTED | login_with_xbox, tests |
| F05 | Entitlement check | `/entitlements/mcstore` | IMPLEMENTED | entitlements check, tests |
| F06 | Profile retrieval | Name, UUID, skins, capes | IMPLEMENTED | profile, skins, capes, tests |
| F07 | Token refresh | Automatic refresh with expiry handling | IMPLEMENTED | refresh with expiry threshold |
| F08 | Multiple accounts + switching | Account list and active selection | IMPLEMENTED | account list, switching, sign-out |
| F09 | Secure credential storage | DPAPI on Windows, degraded-mode elsewhere | IMPLEMENTED | secret store |
| F10 | Secret redaction | Central redactor plus launch-preview redaction | VERIFIED | V001.7 |
| F11 | Yggdrasil-compatible servers | Custom auth server support | IMPLEMENTED | AuthEndpoints is overridable for Yggdrasil servers |
| F12 | Skin/cape preview | Bounded profile fetch and render | IMPLEMENTED | profile exposes skin/cape URLs |
| F13 | Offline/cracked accounts | Deliberately not implemented | BLOCKED EXTERNAL | product brief forbids it |
| F14 | Live Microsoft sign-in | Needs a real account and Azure client ID | BLOCKED EXTERNAL | `HUMAN_ACTION_REQUIRED.md` H1 |

## G. Mod loaders

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| G01 | Fabric version discovery | `/v2/versions/loader/{game}` catalogue | VERIFIED | V002.1 |
| G02 | Fabric profile install | Merge loader profile, resolve maven libraries | VERIFIED | V002.1 |
| G03 | Quilt version discovery | `/v3/versions/loader` catalogue | IMPLEMENTED | same path as Fabric |
| G04 | Quilt profile install | Merge loader profile, resolve maven libraries | IMPLEMENTED | same path as Fabric |
| G05 | NeoForge version discovery | Maven metadata per Minecraft version | VERIFIED | V002.2 |
| G06 | NeoForge installer run | Official installer processors with an argument list | VERIFIED | V002.2 |
| G07 | Forge version discovery | Maven metadata per Minecraft version | IMPLEMENTED | same path as NeoForge |
| G08 | Forge installer run | Official installer processors with an argument list | IMPLEMENTED | same path as NeoForge |
| G09 | Loader update / reinstall / repair | Switch loader version safely | IMPLEMENTED | reinstall by version id |
| G10 | Loader launch behaviour | Loader main class, args, library ordering | VERIFIED | V002.1, V002.2 |
| G11 | OptiFine install | Official installer invocation | NOT STARTED | - |

## H. Launch pipeline

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| H01 | Classpath construction | Ordered libraries + client JAR | VERIFIED | V001.6 |
| H02 | Natives directory | Per instance + version, subdirectories mirrored | VERIFIED | V001.6 |
| H03 | Placeholder substitution | All `${...}` variables, fails closed on unknown | VERIFIED | V001.6, tests |
| H04 | Argument list process launch | `ProcessStartInfo.ArgumentList`, never a shell | VERIFIED | V001.6 |
| H05 | Game directory isolation | Per-instance working directory | VERIFIED | V001.6 |
| H06 | Running instance tracking | Live processes, exit codes, duration | VERIFIED | V001.6 |
| H07 | Live log streaming | stdout/stderr streamed and mirrored to a log file | VERIFIED | V001.6 |
| H08 | Kill running instance | Graceful close then forced kill | VERIFIED | V001.6 |
| H09 | Launch preflight | Java, files, account, memory validation | VERIFIED | V001.6 |
| H10 | Command preview | Resolved command with credentials redacted | VERIFIED | V001.7 |
| H11 | Quick play | `--quickPlaySingleplayer` / `--quickPlayMultiplayer` | IMPLEMENTED | tests |
| H12 | Demo mode | `--demo` toggle | VERIFIED | tests, V001 |

## I. Content: mods, resource packs, shaders

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| I01 | Mod inventory scan | Off-thread scan with metadata extraction | VERIFIED | V003.2 |
| I02 | Mod metadata parsing | fabric/quilt/forge/neoforge/legacy metadata | VERIFIED | V003.2, tests |
| I03 | Enable / disable | Reversible rename, no data loss | VERIFIED | V003.3 |
| I04 | Remove mods | Delete with confirmation | IMPLEMENTED | mod list remove |
| I05 | Bulk operations | Multi-select enable/disable/delete | IMPLEMENTED | per-item actions |
| I06 | Local JAR install | Drag and drop or file picker with validation | IMPLEMENTED | picker wired, drag-drop wired |
| I07 | Mod search/filter/sort | Name, loader, version, source | IMPLEMENTED | list view |
| I08 | Dependency display | Declared dependencies and conflicts | VERIFIED | V003.2 dependency list |
| I09 | Update detection | Compare installed hash against providers | IMPLEMENTED | provider match by version, not wired in UI |
| I10 | Resource pack management | List, enable/disable, delete, reorder | IMPLEMENTED | V004 content tab |
| I11 | Shader pack management | List, enable/disable, delete | IMPLEMENTED | V004 content tab |
| I12 | Datapack management | Per-world datapack listing | IMPLEMENTED | datapack folder listed as content |
| I13 | Screenshot gallery | Browse instance screenshots | IMPLEMENTED | V004 content tab |
| I14 | Content-pack metadata parsing | `pack.mcmeta` compatibility ranges | NOT STARTED | - |

## J. Modrinth integration

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| J01 | Search with facets | Query, project type, category, loader, version | IMPLEMENTED | facets in client and UI |
| J02 | Tag vocabularies | game_version, loader, category, project_type | IMPLEMENTED | tag endpoints in client |
| J03 | Project details | Description, body, gallery, license, authors | IMPLEMENTED | project lookup |
| J04 | Version listing | Per-project versions with filters | VERIFIED | V003.1 |
| J05 | Changelog display | Markdown-rendered changelog | IMPLEMENTED | changelog pane |
| J06 | Dependency resolution | Required first, optional opt-in, cycle guard | VERIFIED | V003.2 |
| J07 | Install into instance | Correct subfolder per project type | VERIFIED | V003.2 |
| J08 | Update installed content | Launcher-installed files are tracked by provider/project/version and updated in place | VERIFIED | V010 |
| J09 | Compatibility guarantee | Never install an incompatible version | VERIFIED | V003.1, V003.2 |
| J10 | Modpack browsing | Modpack projects install as a new instance through the modpack installers | IMPLEMENTED | browser dispatch to MrpackInstaller / CurseForgePackInstaller; live install of a pack project not yet run from the UI |
| J11 | Offline/cached metadata | Network-first cache; a failed call serves cached data and says how old it is | VERIFIED | V009 |

## K. CurseForge integration

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| K01 | API client | Search, project, files, bulk file lookup, dependency resolution | IMPLEMENTED | `CurseForgeClient`, `CurseForgeIds`, `CurseForgeClientTests` |
| K02 | API key configuration | Key in the OS-protected secret store, entered in Settings | IMPLEMENTED | `ProviderCredentialStore`, `ProviderCredentialStoreTests`, SettingsView |
| K03 | Modpack install | `manifest.json` + overrides installer with API file resolution | IMPLEMENTED | `CurseForgePackInstaller`, `CurseForgePackTests` |
| K04 | Retail-file restriction handling | Files without a download URL are reported, never silently skipped | IMPLEMENTED | `CurseForgePackInstaller.PlanFiles` + test |
| K05 | Live CurseForge calls | Requires a user-issued API key | BLOCKED EXTERNAL | `HUMAN_ACTION_REQUIRED.md` H2 |

## L. Modpacks

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| L01 | Install `.mrpack` | Index + downloads + overrides, checksum verified | VERIFIED | V005.1, V005.2 |
| L02 | Install CurseForge zip | manifest.json + overrides + file resolution | IMPLEMENTED | `CurseForgePackInstaller`; archive kind auto-detected on import. Live install BLOCKED EXTERNAL (K05) |
| L03 | Export `.mrpack` | Pack index with hashes and overrides | VERIFIED | V005.3 |
| L04 | Modpack identity | Project, version, provider in instance metadata | IMPLEMENTED | model |
| L05 | Update modpack | Apply a new version, preserve user content | IMPLEMENTED | re-install over an existing instance backs up first |
| L06 | Overrides protection | Never clobber user-edited config without a backup | VERIFIED | V005.1 backup on existing content |
| L07 | Drag-and-drop install | Drop a pack file onto the window | IMPLEMENTED | import button; window-level drop not wired |

## M. Worlds, servers, and multiplayer

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| M01 | NBT reader | Bounds-checked read-only NBT (gzip + raw) | VERIFIED | V006.1, tests |
| M02 | World listing | `level.dat` metadata | VERIFIED | V006.1 |
| M03 | World icon extraction | Render `icon.png` thumbnails | IMPLEMENTED | icon path surfaced in the UI |
| M04 | World backup / restore | Zip, restore, verify | VERIFIED | V006.2, tests |
| M05 | World delete / duplicate / export | Safe destructive operations | VERIFIED | delete/duplicate tests |
| M06 | `servers.dat` management | Read, edit, add, remove | VERIFIED | server list round trip tests |
| M07 | Server status ping | Modern status protocol + legacy fallback | VERIFIED | V006.3 |
| M08 | Server list UI | MOTD, players, latency, version | VERIFIED | V006.3, Servers tab |
| M09 | LAN world discovery | Local discovery assistance | NOT STARTED | - |
| M10 | Internet LAN relay | Requires a relay service Ferrite does not operate | BLOCKED EXTERNAL | `HUMAN_ACTION_REQUIRED.md` H4 |

## N. Diagnostics and observability

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| N01 | Instance log viewer | Tail logs with bounded memory | IMPLEMENTED | log tail in the instance Logs tab |
| N02 | Crash report parsing | Description, cause, stack frames, reported mod list, system details | VERIFIED | V008.1 real 1.8.8 report; `CrashReportTests` for Fabric and NeoForge shapes |
| N03 | Mod attribution from crash | Stack frames matched to installed mods, with the report's own suspects kept separate | VERIFIED | V008.2 (48 real mods, no false positives); `CrashReportTests` |
| N04 | Diagnostics bundle export | Redacted zip of launcher logs, operations, instance metadata, game logs, and crash analysis | VERIFIED | V008.3; `DiagnosticsTests` |
| N05 | Installation diagnose + repair | Detect and fix missing/corrupt files | VERIFIED | V001.4, V001.5 |
| N06 | Operation log | Recent operations with outcome and duration, persisted and bounded | VERIFIED | V008.4; `DiagnosticsTests` |

## O. Launcher settings, storage, and updates

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| O01 | Settings screen | All persisted settings with validation | VERIFIED | V004 settings page |
| O02 | Download concurrency setting | Bounded, applied live | IMPLEMENTED | settings value |
| O03 | Mirror configuration | Per-endpoint host overrides | IMPLEMENTED | settings model |
| O04 | Proxy configuration | Applied to the HTTP stack | IMPLEMENTED | HttpService proxy |
| O05 | Cache management | Size reporting and safe cleanup | VERIFIED | V004 storage section |
| O06 | Data location control | Move the data root with validation | IMPLEMENTED | FERRITE_HOME |
| O07 | Update check | Signed manifest check over TLS | NOT STARTED | - |
| O08 | Update staging | Download, verify, prepare hand-off | NOT STARTED | - |
| O09 | Update feed | Requires externally hosted infrastructure | BLOCKED EXTERNAL | `HUMAN_ACTION_REQUIRED.md` H3 |
| O10 | Localisation | English and Polish, switchable | NOT STARTED | setting stored, no translated resources |
| O11 | Accessibility | Keyboard navigation, focus, labels, contrast | IMPLEMENTED | keyboard reachable controls, themed focus |

## P. Cross-cutting quality

| ID | Capability | Our implementation | Status | Evidence |
| --- | --- | --- | --- | --- |
| P01 | Archive safety | Zip-slip, absolute path, traversal, link rejection | VERIFIED | tests |
| P02 | Path safety | Canonicalisation and containment before writes | VERIFIED | tests |
| P03 | Malicious metadata defence | Size caps, depth caps, parse failures as typed errors | IMPLEMENTED | tests |
| P04 | Network resilience | Offline fallback, retries, timeouts | VERIFIED | V001.2, tests |
| P05 | Performance with large packs | Caching, incremental scans, no UI-thread blocking | IMPLEMENTED | V001.3 |
| P06 | Visual QA pass | Full screen/state sweep with fixes | VERIFIED | V004 |
| P07 | Security audit | Adversarial review of untrusted paths | VERIFIED | docs/SECURITY.md audit log |
| P08 | Repository hygiene | `.gitignore`, no secrets, no build output committed | VERIFIED | git history |

---

## Summary

| Status | Count |
| --- | --- |
| NOT STARTED | 7 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 63 |
| VERIFIED | 97 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 8 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 63 |
| VERIFIED | 96 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 9 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 63 |
| VERIFIED | 95 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 13 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 63 |
| VERIFIED | 91 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 19 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 57 |
| VERIFIED | 91 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 24 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 58 |
| VERIFIED | 85 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 36 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 48 |
| VERIFIED | 83 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 46 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 45 |
| VERIFIED | 76 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 52 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 43 |
| VERIFIED | 72 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 52 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 43 |
| VERIFIED | 72 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 52 |
| IN PROGRESS | 0 |
| IMPLEMENTED | 43 |
| VERIFIED | 72 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 90 |
| IN PROGRESS | 2 |
| IMPLEMENTED | 25 |
| VERIFIED | 50 |
| BLOCKED EXTERNAL | 5 |
| --- | --- |
| NOT STARTED | 74 |
| IN PROGRESS | 2 |
| IMPLEMENTED | 30 |
| VERIFIED | 38 |
| BLOCKED EXTERNAL | 4 |
