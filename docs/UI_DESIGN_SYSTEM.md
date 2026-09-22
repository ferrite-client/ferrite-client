# Ferrite UI Design System

This is the contract every Ferrite screen is built against. It describes what is actually in
`src/Ferrite.App/Styles`, so if a value here is wrong the fix is in the resource files first and in
this document second.

Files:

- `Styles/Palette.axaml` - colours, once per theme. The only place a hex value is allowed.
- `Styles/Tokens.axaml` - spacing, radii, control sizes, motion durations. Theme-invariant.
- `Styles/Icons.axaml` - the icon set, as geometry.
- `Styles/ControlThemes.axaml` - implicit control themes that replace a shipped control's own look.
- `Styles/Controls.Templates.axaml` - control templates and the variants built on them.
- `Styles/Controls.axaml` - typography, surfaces, inputs, badges, banners, and list behaviour.

## Direction

Deep graphite surfaces with a warm ferrite/oxide accent. The accent is the brand and the selection
language; it is never used to mean a state. The interface is dense enough for a two-hundred-mod pack
and quiet enough that nothing shouts.

Nothing here uses rainbow or RGB styling, neon, glowing borders, large gradients, glassmorphism,
blur, decorative orbs, or the rounded-everything look.

## Colour

Every colour is a `Ferrite*` key in `Palette.axaml`, defined once per theme. Nothing else hard-codes
a colour.

### Surfaces

| Token | Role |
| --- | --- |
| `FerriteBg` | The window backdrop, behind everything. Not pure black. |
| `FerriteSurfaceSunken` | Wells: inputs, the navigation rail, the status bar. |
| `FerriteSurface` | The ordinary page surface. |
| `FerriteSurfaceRaised` | Cards, panels, the hero, and the chrome of an overlay. |
| `FerriteSurfaceOverlay` | Menus, tooltips, and dialogs that float above the page. |
| `FerriteHover` / `FerritePressed` | Pointer states for rows and quiet controls. |
| `FerriteClear` | Zero-alpha black. See the note under Motion. |

Hierarchy comes from surface separation first, spacing second, and a border last. A border around
every grouping is the failure mode this palette exists to avoid.

### Text

| Token | Role |
| --- | --- |
| `FerriteText` | Primary text. |
| `FerriteTextMuted` | Secondary text, labels, metadata. |
| `FerriteTextFaint` | Tertiary text at 12px and up. |
| `FerriteTextOnAccent` / `FerriteAccentText` | Text on an accent fill. |

AA is enforced by a test, not by eye:
`AccessibilityTests.Every_text_colour_clears_wcag_aa_on_both_surfaces` parses `Palette.axaml` and
fails the build if a text or semantic colour drops below 4.5:1 against any of the three surfaces, or
if accent-on-accent text is unreadable.

### Accent and semantics

| Token | Meaning |
| --- | --- |
| `FerriteAccent` (+ `Hover`, `Pressed`, `Soft`) | The brand, selection, and the one primary action on a screen. |
| `FerriteSuccess` (+ `Soft`) | Completed, healthy, enabled. |
| `FerriteWarning` (+ `Soft`) | Worth a look, not a failure. |
| `FerriteDanger` (+ `Soft`) | Failure and destructive actions. |
| `FerriteInfo` (+ `Soft`) | Neutral information, update available, downloading. |
| `FerriteRunning` | The instance is running. |
| `FerriteDownloading` | Transfer in progress. |

State never borrows the accent. "Enabled" is green, "update available" is blue, "failed" is red, and
the accent means "this is selected" or "this is the main action".

The Fluent theme's own controls (check boxes, switches, sliders, selection) are pointed at the
ferrite accent through the `SystemAccentColor*` overrides in `Palette.axaml`, so a stock control is
on-brand without being retemplated.

## Typography

One family (`Inter`, shipped with the application) and a fixed scale. Hierarchy comes from size and
weight, never from colour alone, and never from letter spacing.

| Class | Size | Weight | Used for |
| --- | --- | --- | --- |
| `.display` | 26 | SemiBold | The instance hero's name. At most one per screen. |
| `.h1` | 20 | SemiBold | Page and dialog titles. |
| `.h2` | 15 | SemiBold | Section titles. |
| `.h3` | 13 | SemiBold | Row and item titles. |
| `.stat` | 18 | SemiBold | A single number that matters (a device code). |
| body (default) | 13 | Regular | Everything else. |
| `.meta` | 12 | Regular | Secondary metadata. |
| `.faint` | 12 | Regular, faint | Tertiary metadata. |
| `.label` | 11 | SemiBold | Field and section labels. |
| `.mono` | 12 | Regular, monospace | Paths, versions, logs, commands. |

Not everything is semibold: a row title is `.h3`, a table cell is body or `.meta`, and only a page
title is `.h1`.

Long text: prose wraps by default. A dense row sets `TextWrapping="NoWrap"` with a trimming mode and
a tooltip carrying the full value, so a row never grows because a mod has a long name.

## Spacing, radii, and sizes

Spacing is a scale, not a per-view decision: `4, 6, 8, 12, 16, 20, 24, 32` (`Space1`-`Space8` for
`Spacing`, and `Pad1`-`Pad8` as `Thickness` for padding and margin). Roughly: 4-8 inside a control or
between related lines, 12-16 between groups, 20-32 between sections.

| Radius | Value | Used for |
| --- | --- | --- |
| `RadiusSm` | 6 | Badges, chips, small controls, segment ends. |
| `RadiusMd` | 8 | Buttons, inputs, rows, quiet panels. |
| `RadiusLg` | 12 | Cards, the hero, dialogs. |
| `RadiusPill` | 999 | Compact statuses, tags, and counts only. |

A pill means "this is a label", not "this is a container".

Control heights are `ControlHeightSm` (28), `ControlHeightMd` (34), and `ControlHeightLg` (40). The
rail is `RailWidth` (236) or `RailWidthCompact` (64); the title bar is 40 and the status bar 30.

## Icons

`Icons.axaml` holds a hand-authored set on a 24x24 grid: one stroke weight, round caps and joins,
and a filled variant only where a solid shape reads better (Play, Stop, Bolt). The geometry is the
project's own, so the icons ship under the application's licence with no third-party attribution.

Rendering goes through `Controls/IconGlyph.cs`, a templated control that inherits `Foreground`. That
is why an icon inside a primary button is dark-on-accent while the same icon in a metadata row is
muted, with no brush repeated at the call site. Icons are 13-18px in chrome and rows and 26-34px in
an empty state, and an icon-only button always carries a name and a tooltip.

## Components

### Buttons

| Variant | Appearance | Use |
| --- | --- | --- |
| default | Raised surface, border | The ordinary action. |
| `.primary` | Accent fill, accent text | Exactly one per screen or region: Play, Install, Save. |
| `.danger` | Danger label and border, danger-soft wash on hover | Destructive but not urgent. |
| `.danger-solid` | Danger fill | The confirm button inside a destructive dialog. |
| `.ghost` | Borderless until hover | Secondary and tertiary actions, toolbar icons. |
| `.icon-button` (+ `.sm`, `.lg`) | Square, centred icon | Toolbars and the title bar. Needs a name and tooltip. |
| `.chip` | Text-scale, no fill | An inline action in a dense row. |
| `.segment` (+ `.active`) | Grouped exclusive choice | View switches and short filters. |
| `.nav-footer` (+ `.active`) | Matches a rail row | The account switcher and Settings. |

The accent is used at most once per screen or region, and it always answers "what is the one thing to
do here?". A section button that is merely *available* is a default button, not a primary one: on the
instance page "Choose a structure file" and "Prepare files" are ordinary buttons, while Play and
"Start server" are the actions that carry the accent.

### Inputs

Text boxes, combo boxes, and numeric inputs share one shape: sunken fill, 1px border, `RadiusMd`, a
34px minimum height, and an accent border on focus. The focus border is drawn rather than animated,
so nothing shifts under the pointer.

### Lists and rows

Six behaviours, chosen by what the list is:

- `ListBox` (default): selection is a soft accent fill with a bright label.
- `ListBox.rail`: the navigation rail. 38px rows, icon plus label.
- `ListBox.dense`: data rows (mods, worlds, servers, operations). 40px rows.
- `ListBox.segments`: an exclusive switch.
- `ListBox.results`: search results. Selection lifts the row and marks the leading edge with a 3px
  accent bar rather than filling it, so results do not outshout the project hero beside them.
- `ListBox.log`: monospace log lines, near-zero padding, selection tint only.

A large list is virtualised. The library grid is expressed as rows of cards precisely so a
virtualising panel still applies, and artwork is decoded when a container is realised and released
when it is cleared.

### Badges, banners, dividers, surfaces

- `Border.badge` (+ `.accent`, `.success`, `.warning`, `.danger`, `.info`, `.plain`): a pill for a
  compact status or identity. The inner text is 11px semibold and never wraps.
- `Border.banner` (+ tones): an inline message that keeps its action beside it, for errors and
  warnings that should persist until dismissed.
- `Border.card`: an independent object (a runtime, an account, a world). Padding 16.
- `Border.panel`: a quiet grouping inside something else. Sunken, no border, padding 12.
- `Border.overlay`: a floating surface (dialog, drawer). Overlay fill, border, high shadow.
- `Border.divider`: a 1px rule.

Cards are not nested, and a row of items is not a card per item unless the item is genuinely an
object. Mods, downloads, and log lines are rows.

### Overlays

Menus, tooltips, and dialogs use the overlay surface, `RadiusMd` or `RadiusLg`, a border, and a
shadow. Dialogs are reserved for decisions that must block: destructive confirmation and multi-field
creation. Everything else is an inline control, a menu, or a drawer.

There is exactly one confirmation dialog in the product. `MainWindowViewModel.ConfirmAsync` shows a
`ConfirmationViewModel` over the whole window and returns the answer, so a caller reads as
`if (!await ConfirmAsync(...)) return;` and the guarded action cannot run before the answer. A second
question supersedes the first rather than stacking, and the superseded one answers "no". It guards
deleting an instance, deleting a world, and signing an account out - the decisions that are hard to
reverse from the user's point of view. Per-file removals are not guarded, because they are one click
inside a list and they move the file to the launcher's backups folder; guarding those would be the
dialog fatigue this rule exists to prevent.

Toasts (`ToastViewModel`, `ToastKind`) carry transient outcomes only - the success, warning, or
failure of something the user asked for. A toast never carries a decision and never replaces the
persistent error banner.

## States

- **Empty**: an icon, one sentence saying what to do next, and the action itself where one exists.
- **Loading**: skeletons or a real progress bar. An indefinite spinner is a last resort, and a
  progress bar only ever shows measured progress.
- **Error**: what failed, what to do about it, and the technical detail where the launcher knows one.
  No raw stack traces as primary UI, and no "something went wrong" when Ferrite knows what went wrong.
- **Disabled**: 45% opacity, with the reason visible somewhere on screen.
- **Destructive**: danger colour, a confirmation that names the object, and a recoverable outcome
  where the launcher can offer one (delete moves the instance to backups).

## Motion

Durations are `0.12s` for state feedback, `0.18s` for the rail and overlays, and `0.24s` for a
deliberate transition. Motion is used for hover and press feedback, selection changes, the rail
collapse, and expanding detail. Nothing animates on load, and nothing delays input.

One trap worth recording: Avalonia's `Transparent` brush is white with zero alpha, so animating a
background from it flashes pale on the way to the real colour. `FerriteClear` is zero-alpha black,
and every animated background starts from that instead.

## Accessibility

- Every interactive control has a name or a label; `AccessibilityTests` fails the build otherwise.
- Keyboard focus is visible on every focusable control (`:focus-visible`, accent border).
- Tab order follows reading order; the rail, the page content, and the status bar are separate stops.
- Contrast is enforced by test at 4.5:1 against all three surfaces.
- Primary workflows do not depend on hover: every hover-only affordance has a keyboard or menu path.
