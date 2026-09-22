# Ferrite UI Feature Inventory

Purpose: record every user-facing capability that existed before the visual overhaul, where it lived,
and where it lives afterwards. The rule for this document is "no silent feature loss": each row is
either still reachable, consolidated into a better workflow, or explicitly marked obsolete.

Status values:

- `kept` - same capability, same or clearer home.
- `moved` - same capability, new home in the information architecture.
- `consolidated` - merged with another entry point to remove duplication.
- `obsolete` - the underlying feature was already removed on purpose; the row explains why.

## Global shell

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Library destination | Sidebar text list | Nav rail, Library | kept |
| Browse content destination | Sidebar text list | Nav rail, Discover | moved |
| Java runtime management | Sidebar text list, `JavaView` | Settings > Java | moved |
| Accounts destination | Sidebar text list | Account switcher in the rail footer + Accounts page | moved |
| Settings destination | Sidebar text list | Nav rail footer, Settings | kept |
| Active account summary | Bottom of sidebar | Account switcher button in the rail footer | kept |
| Running instance indicator | Bottom of sidebar | Status bar + instance hero running state | kept |
| Quick-action palette (Ctrl+K) | Overlay panel | Search overlay with grouped results | kept |
| Activity/progress strip | Bottom status bar | Downloads drawer + status bar summary | consolidated |
| Error banner | Floating card over content | Inline banner in the status area with dismiss | kept |
| Theme switching (Dark/Light/System) | Settings > Appearance | Settings > Appearance, unchanged behaviour | kept |
| Language switching (EN/PL) | Settings > Appearance | Settings > Appearance, unchanged behaviour | kept |

## Library

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Instance list | Single-column list of wide cards | Grid of artwork cards + optional list view | kept |
| Instance search | Toolbar text box | Toolbar search, unchanged behaviour | kept |
| Sort options | Toolbar combo box | Toolbar control with the same four sorts | kept |
| Folder grouping / filter | Toolbar combo box | Toolbar folder filter + per-card folder chip | kept |
| Create instance (name, version, snapshots, loader, loader version) | Modal card over the list | Create dialog (overlay panel) with the same fields | kept |
| Import modpack | Toolbar button + file picker | Toolbar primary action, same flow | kept |
| Play / Stop | Per-card buttons | Per-card primary action + hero Play | kept |
| Manage (open instance detail) | Per-card button | Card click / Manage action | kept |
| Rename | Per-card button + prompt | Card context menu + prompt | kept |
| Clone | Per-card button + prompt | Card context menu + prompt | kept |
| Open folder | Per-card button | Card context menu | kept |
| Repair | Per-card button | Card context menu + instance Overview | kept |
| Delete (to backups) | Per-card button | Card context menu + confirmation | kept |
| Move to folder | Per-card chip button + prompt | Card context menu + prompt | kept |
| Memory / size / last played metadata | Text line on card | Compact metadata row on the card | kept |
| Modpack identity | Text line on card | Badge on the card | kept |
| Empty library state | One muted sentence | Empty state with Create/Import actions | kept |

## Instance detail

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Back to library | Header button | Sub-nav header back button | kept |
| Verify installation | Header button | Overview + Tools menu | kept |
| Repair installation | Header button | Overview + Tools menu | kept |
| Open instance folder | Header button | Overview + Tools menu | kept |
| Export pack | Header button | Tools menu | kept |
| Apply pack over instance | Header button | Tools menu | kept |
| Save instance settings | Header button | Settings tab save | kept |
| Launch state (ready/running/progress) | Status bar text | Instance hero play state with stage text and progress | kept |

## Instance sub-sections

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Settings: loader switch, loader version | Tab | Overview (loader card) + Settings tab | kept |
| Settings: OptiFine install, LabyMod install | Tab | Overview loader card | kept |
| Settings: Java runtime, min/max memory, window size | Tab | Settings tab | kept |
| Settings: default server, folder, accent colour, background image | Tab | Settings tab | kept |
| Settings: demo mode, EULA, JVM args, game args | Tab | Settings tab | kept |
| Mods: list, search, loader filter, group filter, sort | Tab | Mods tab with dense rows | kept |
| Mods: enable/disable/add/remove | Tab | Mods tab row actions + selection action bar | kept |
| Mods: bulk enable/disable/remove | Tab | Mods tab selection action bar | kept |
| Mods: groups (create/remove) | Tab | Mods tab group filter + manage dialog | kept |
| Content: available updates, select, update | Tab | Content tab | kept |
| Content: resource packs, shader packs, datapacks, screenshots | Files tab | Files tab | kept |
| Worlds: list, backup, play, duplicate, delete, open saves | Tab | Worlds tab | kept |
| Servers: saved servers, add, ping all, LAN discovery | Tab | Servers tab | kept |
| Logs: launcher log, crash reports, crash analysis | Tab | Logs tab with severity filter and search | kept |
| Structure: preview a structure file | Tab | Structure tab | kept |
| Map: render world chunks, select, delete, copy | Tab | Map tab | kept |
| Local server: settings, prepare, start, stop, export | Tab | Local server tab | kept |
| Assistant: instance check | Tab | Assistant tab (kept, action moved into Overview "Check instance") | kept |

## Discover (formerly Browse)

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Provider selection (Modrinth/CurseForge/FTB) | Combo box | Toolbar segmented provider control | kept |
| Search | Toolbar | Search field with results count | kept |
| Content-type / category / version / loader / order filters | Combo row | Toolbar filter row | kept |
| Result list | Left list panel | Result cards with artwork | kept |
| Project detail (about, body, gallery, changelog, versions) | Right panel | Project detail view with hero | kept |
| Install into instance, optional dependencies, install | Right panel | Project detail install panel | kept |
| Save / unsave project, saved list | Toolbar toggle | Segmented view switch (Results / Saved) | kept |
| Open project page | Right panel button | Project detail action | kept |
| Cached/stale result note | Text line | Inline note under the toolbar | kept |

## Accounts

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Microsoft device-code sign in | Page buttons | Account page, same flow | kept |
| Browser sign in | Page button | Account page, same flow | kept |
| Yggdrasil sign in | Inline fields | Account page, same fields | kept |
| Account list with skin/cape preview | Card list | Account cards with avatar | kept |
| Set active account | Per-row button | Per-row action | kept |
| Sign out / remove | Per-row button | Per-row action with confirmation | kept |
| Credential-degradation warning | Footer note | Inline banner | kept |

## Settings

| Capability | Before | After | Status |
| --- | --- | --- | --- |
| Appearance: theme, language, snapshots, historical versions, default memory, log retention | One long page | Appearance category | kept |
| Network: concurrent downloads, proxy, mirrors | One long page | Network category | kept |
| Storage: data root, move folder, cache size, open folders, clear cache | One long page | Storage category | kept |
| Launcher updates: feed URL, check, stage, handoff command | One long page | Updates category | kept |
| Diagnostics: recent operations, support bundle export, notes | One long page | Diagnostics category | kept |
| Java runtime management | Global page | Java category (reuses the Java view) | moved |
| Save settings | Page footer button | Sticky category footer / header action | kept |

## Obsolete rows

| Capability | Reason |
| --- | --- |
| Microsoft client-ID field | Removed on purpose: the identity is bundled and no longer user-editable. Confirmed by `ShellRenderingTests.Settings_and_accounts_do_not_render_developer_credential_controls`. |
| CurseForge API-key field | Removed on purpose: the credential ships in one immutable bundled source. |
| Provider credential store | Removed from Settings; the packaged configuration is the only source. |
