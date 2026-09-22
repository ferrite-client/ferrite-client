# Ferrite UI Redesign

What changed, why, and what is still open. The design system itself is in
[UI_DESIGN_SYSTEM.md](UI_DESIGN_SYSTEM.md); what happened to each existing capability is in
[UI_FEATURE_INVENTORY.md](UI_FEATURE_INVENTORY.md).

## The problem

The launcher worked and looked like a default Avalonia application. One palette and one style sheet
covered a handful of elements; every tab was its own invention. Navigation was a text list. The
instance page was a horizontal strip of eleven tabs above a form. Lists that can hold hundreds of
items were rendered as a vertical stack of cards. There was no artwork anywhere, no notification
system, and no view of what the launcher was actually doing beyond one sentence in a status bar.

The brief was to make it one deliberate product for modded Minecraft, without rebuilding the
backend or losing a single capability.

## Information architecture

Global navigation is three destinations: **Library**, **Discover**, **Downloads**. The account
switcher and **Settings** sit in the rail footer, visually separated from primary navigation, and
the account switcher opens a list of accounts with their state rather than a separate page visit.

Java runtime management moved out of global navigation and into Settings, because it is launcher
configuration rather than a place you go. The Accounts page remains the full manager (sign-in,
switching, sign-out) reachable from both the account flyout and the quick actions.

Everything instance-specific lives inside the instance. The instance page's sections grew rather
than the global rail's: eleven destinations in one instance is normal for a modpack, eleven global
destinations is not.

## Application shell

A custom title bar drawn through Avalonia's window-decoration roles. The drag area is marked as the
title-bar role and the interactive element inside it as a user element, so the platform still
handles moving, double-click to maximise, drag-to-edge snapping, the Snap Layouts flyout, resize
borders, multi-monitor placement, and per-monitor DPI. The caption buttons belong to the theme's
drawn decorations; the launcher does not reimplement minimising or closing, because doing that is
how a custom title bar becomes an unreliable one.

The rail collapses to icons at the user's request and reflows to a 64px strip, with tooltips on
every entry. A Downloads entry carries an activity badge. The status bar keeps the one-line status,
a measured progress bar, the running instance, an activity drawer toggle, and the quick-actions
entry (also `Ctrl+K`).

Transient outcomes now surface as toasts rather than only as a status line, and the persistent error
banner stays where it was: errors that need reading stay on screen until dismissed.

## Navigation design

| Where | Before | After |
| --- | --- | --- |
| Global | Library, Browse, Java, Accounts, Settings | Library, Discover, Downloads; account and Settings in the footer |
| Instance | 11 horizontal tabs | 11-item vertical rail with icons and a left accent bar |
| Discover | Provider/saved text buttons | A segmented view switch |
| Settings | One long scrolling page | Six categories with a category rail |

The instance rail uses a `TabControl` underneath, so `SelectedIndex`, keyboard traversal, and the
automation peer a tab strip already provides are kept; only the presentation changed.

## Library design

A grid of artwork cards by default, with a list view for people who want more rows on screen. Each
card carries artwork (or a fallback tile tinted by the instance's own accent), the instance name,
Minecraft version and loader as badges, the modpack identity as an accent badge, when it was last
played, its size on disk, a Play (or Stop) action, and a menu holding rename, clone, folder, open
folder, repair, and delete.

Per-card buttons were reduced from eight to two plus a menu. The card artwork is a button that opens
the instance, and it carries the accessible name "Open <instance>".

Scale: the grid is expressed as rows of cards so a virtualising panel still applies, and the column
count is measured from the window rather than fixed. Artwork is decoded when a card's container is
realised and released when it is cleared, and it is decoded at card size, so a library of hundreds
of instances holds images for the viewport rather than for the whole collection.

Long names trim with a tooltip rather than wrapping, so cards stay aligned.

## Instance design

The page opens on a hero: artwork behind a surface wash, the instance name at display size, version
and loader badges, modpack identity, a one-line state, and a single dominant Play button that grows
into a progress bar while the launcher works and a Stop button once the game is running. The
progress it shows is the launcher's real operation, not a fabricated stage animation.

Secondary actions moved into a menu so that nothing competes with Play. The artwork is dimmed by a
surface wash rather than a fixed gradient, so the same markup stays readable in both themes.

The eleven sections became a vertical rail. The mod manager became a dense columned table - name,
version, loader, size, state - with selection checkboxes and a bulk action bar that appears only
when something is selected, and per-row actions reduced to an enable/disable chip plus a menu. The
log view gained a severity filter, a search box, following, copy, and an open-folder action, and
distinguishes "no log yet" from "the filter excluded everything".

## Content discovery

Discover is a two-pane browser: a filter bar (provider, content type, category, Minecraft version,
loader, order), a result list of content rows, and a project detail pane. The detail pane leads with
the provider's own project icon, title, description, type, provider, and author, then an install
panel (target instance, version, optional dependencies), the project body, the gallery, and the
changelog, with Save, Open project page, and a single primary Install.

Result selection lifts the row and marks its leading edge instead of filling it with the accent, so
the results do not outshout the project hero next to them. Provider errors, cache notes, and
provider limits stay visible under the filters rather than replacing the results.

## Downloads

Downloads is a new destination built entirely on the operation log the diagnostics section already
kept, so the two can never disagree about what happened. It shows what is running now with the real
progress fraction, and a filterable, searchable history of finished operations with their outcome,
start time, and duration. A drawer in the shell shows the same information without navigating away,
which is what the brief asked for.

## Settings and accounts

Settings is categorised (Appearance, Java, Network, Storage, Launcher updates, Diagnostics) with the
category rail on the left and a save action pinned at the bottom of the content. Java moved in
beside the other global configuration.

Accounts is a two-pane page: the account list with avatar, name, UUID, kind, an Active badge, and a
Use or Sign out action; and an "add an account" panel with the two Microsoft flows, the device code
with its own label, and the third-party server form. The credential-degradation warning is an inline
banner rather than a footer sentence.

## Major UX decisions

1. **One primary action per region.** Play on an instance, Install on a project, Save in settings.
   Everything else is default, ghost, or behind a menu.
2. **Rows, not cards, where items are numerous.** The inventory's "avoid card hell" is enforced by
   using `Border.panel` for groupings and reserving `Border.card` for genuinely independent objects.
3. **State has its own colour.** The accent is selection and primary action only.
4. **Progressive disclosure.** The library shows state and a Play button; mod lists, repair, logs,
   and diagnostics live one level in.
5. **Artwork with a fallback.** An instance without artwork gets a tile tinted by its own accent and
   its initial, so the grid never looks broken and never looks empty.
6. **Real data only.** Downloads shows the operation log; the hero shows the launcher's own progress
   stage. Nothing fabricates progress or a status.
7. **No silent feature loss.** The inventory maps every pre-existing capability to where it lives
   now, and the tests that asserted the old structure were updated to the new one rather than
   deleted.

## Visual QA results

Rendering is done headlessly against the real views with `FERRITE_UI_SHOTS` set, so the frames come
from the shipped XAML and the shipped styles rather than from mockups. Frames were captured and
inspected for the shell, the populated and empty library (grid and list), the instance hero with its
rail, each instance section, Discover with and without results, the project detail, Accounts,
Settings, Java, the crash-report analysis, and the light theme.

Defects found this way and fixed:

1. The base `Button.primary` rule was missing, so every primary button rendered as an ordinary one.
2. Spacing tokens typed as `Double` were used where `Thickness` was required, which failed the
   layout at render time rather than at compile time.
3. Background transitions interpolated from `Transparent` (white, zero alpha), so a newly selected
   rail row flashed pale. Fixed by adding `FerriteClear`.
4. Library cards had different heights, so Play buttons did not line up across a row.
5. Card artwork tiles were too dark to read as a placeholder.

## DPI, theme, and localization QA

- Both themes are designed, not inverted. The light theme has its own surfaces, its own text tones
  (faint text was darkened until it cleared AA against the light sunken surface), and its own
  semantic soft washes, and it is rendered in the screenshot pass.
- The rail collapses, the library column count is measured from the window, the instance rail is
  fixed width so content grids keep their columns, and the minimum supported window (1024x680) is
  covered by a rendering test.
- Every new string was added to both the English and Polish tables, and a test fails the build if a
  key is used in one place and missing in the other, if the placeholders differ between languages,
  if a key is defined and never used, or if a view asks for a key that does not exist. The Polish
  shell is rendered and asserted.

## Unresolved issues

- **File-picker filter descriptions** are still English ("Minecraft mods", "Zip archive"). The dialog
  titles are localized; the filter labels are not.
- **Snap Layouts and DPI behaviour** are implemented through the platform's own decoration roles,
  which is the supported path, but they cannot be exercised in a headless renderer. They need a
  manual pass on Windows at 100%, 125%, 150%, and 200% with the window maximised, restored, and
  dragged between monitors.
- **Result rows use a content-type glyph rather than per-project artwork.** The project detail pane
  does load and show the provider's real icon. Fetching and caching an icon for every search result
  needs a per-result view model and a bounded image cache; the search result list is currently bound
  directly to the provider's summary records.
- **Downloads shows history, not per-transfer rows.** The launcher runs one operation at a time, so
  "active" is a single entry with a real fraction. Per-file rows, retry, and cancellation would need
  the download engine to expose those operations, and inventing buttons for behaviour that does not
  exist would be worse than not having them.
- **Mod rows keep an enable/disable chip per row.** A switch would be the orthogonally correct
  control, but enabling a mod renames the file on disk and reports failure per file, so the action
  stays an explicit command rather than a two-way toggle that can silently fail.
