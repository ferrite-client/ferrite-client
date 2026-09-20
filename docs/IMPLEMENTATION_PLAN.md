# Implementation Plan

Ordered milestones. Each milestone ends with build + test + documentation update.

## M0 - Repository and governance (done)

- Solution, projects, build props, `.gitignore`, `.editorconfig`.
- `FEATURE_PARITY.md`, `docs/RESEARCH.md`, `docs/ARCHITECTURE.md`, this plan.

## M1 - Core foundations

Data root layout, platform facts, atomic JSON storage, settings schema with migration,
structured JSON-lines logging with redaction, path-safety helpers, hashing helpers,
archive extraction with zip-slip defence, HTTP client factory, download engine.

Exit criteria: unit tests for path safety, archive safety, atomic writes, hashing, and the
download engine (against a local HTTP fixture), all green.

## M2 - Minecraft version management and install

Version manifest cache, version documents, inheritance, rule evaluation, artefact graph
planning, native extraction, asset index handling, install manifest, integrity verification
and repair.

Exit criteria: a real vanilla install completes and verifies for a current release.

## M3 - Java management

Discovery, version/arch/vendor detection, compatibility matrix, global and per-instance
selection, Mojang runtime provisioning, JVM/RAM editor model.

Exit criteria: discovered runtimes parsed correctly on this machine; provisioning downloads a
runtime and reports its version.

## M4 - Launch pipeline

Classpath, natives, placeholders, argument assembly, process launch with `ArgumentList`,
process tracking, log streaming, command preview with redaction.

Exit criteria: real Minecraft reaches the main menu (or a documented, precise external
blocker), and a deterministic unit test proves the generated argument vector for fixture
versions.

## M5 - Instances

Instance model and store, create/clone/rename/delete/import/export/archive, per-instance
settings, isolation guarantees, disk usage, repair integration.

Exit criteria: create, clone, delete, export and re-import an instance; verify isolation.

## M6 - Accounts

Microsoft device-code flow, PKCE flow, Xbox Live, XSTS, Minecraft services, entitlements,
profile, refresh, multi-account, DPAPI-protected storage, Yggdrasil-compatible servers.

Exit criteria: unit tests over the full chain with a mock HTTP boundary plus live
reachability probes; live sign-in recorded as BLOCKED EXTERNAL until a client ID exists.

## M7 - Mod loaders

Fabric, Quilt, NeoForge, Forge, OptiFine installation and launch behaviour.

Exit criteria: real Fabric and NeoForge installs produce launchable instances.

## M8 - Mod/content management

Mod scanning and metadata parsing (fabric/quilt/forge/neoforge/legacy), enable/disable,
delete, bulk operations, local install, resource packs, shaders, datapacks, screenshots.

Exit criteria: a real modded instance launches with locally installed mods; disable/enable
round-trips without data loss.

## M9 - Modrinth

Search, facets, project, versions, dependencies, install, updates, modpacks.

Exit criteria: install a real mod and a real modpack from Modrinth; verify compatibility rules.

## M10 - Modpacks

`.mrpack` install/export, CurseForge zip install, identity, update, overrides protection,
drag and drop.

Exit criteria: round-trip export then re-import; modpack update preserves user content.

## M11 - CurseForge

API client, key configuration, modpack install, retail-file restrictions.

Exit criteria: everything except live calls verified with fixtures; live calls BLOCKED
EXTERNAL pending a user API key.

## M12 - Worlds, servers, multiplayer

NBT reader, world listing/backup/restore/delete, `servers.dat`, server ping, LAN assistance.

Exit criteria: real worlds from the machine's own `.minecraft` list correctly; a backup and
restore round-trip verifies; a real server pings.

## M13 - Diagnostics

Log viewer, crash parsing, mod attribution, repair, diagnostics bundle.

Exit criteria: a real crash report parses into a structured explanation.

## M14 - UI, settings, localisation, accessibility, visual QA

All screens wired to real services, theme switching, settings, cache management, localisation,
accessibility, and a full visual QA sweep with fixes.

## M15 - Packaging and audits

Publish profiles, packaged release, self-update check/stage, security audit, final parity
audit, final report.
