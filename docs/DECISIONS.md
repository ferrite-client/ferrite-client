# Decisions

Decisions are recorded with the reason and the alternatives that were rejected, so a later
reader (or a compacted context) can tell intent from accident.

## D001 - Product name and identity

**Decision.** The product is "Ferrite", an original launcher. Namespaces are `Ferrite.*`.

**Why.** XMCL is a functional reference only; the brief requires an original implementation,
architecture, and visual identity. Ferrite is our own name, palette (graphite + copper),
iconography, and copy. No XMCL assets, screenshots, or branding are used.

## D002 - Avalonia 12.1.2 on .NET 10

**Decision.** Target `net10.0` with Avalonia 12.1.2 (latest stable at the time of writing,
verified to ship `net10.0` and `net8.0` assets).

**Alternatives.** Avalonia 11.3.x was available as a fallback line; .NET 8 as a lower target.
Neither was needed: the toolchain here is SDK 10.0.201 with runtimes 8 and 10, and the shell
builds and runs.

**Consequence.** `Avalonia.Diagnostics` has no 12.x release, so the DevTools package is not
referenced. This is a development convenience only, not product functionality.

## D003 - Three projects, not a project-per-layer

**Decision.** `Ferrite.Core`, `Ferrite.App`, `Ferrite.Core.Tests`.

**Why.** The brief warns against enterprise abstraction and giant dependency graphs. Core is
organised by folder/namespace; that keeps the build fast and the dependency direction obvious
without inventing projects that never ship separately.

## D004 - JSON files instead of a database

**Decision.** Launcher state is schema-versioned JSON written atomically. No SQLite.

**Why.** State is small (settings, accounts, instances), and after a crash a human-readable
file is repairable while a corrupt database is not. It also removes a native dependency.
Large collections such as installed mods are derived by scanning the instance directory, with
a cache for metadata, so there is no second source of truth to desynchronise.

**Rejected.** SQLite for mod inventories: it would add a native dependency and a migration
burden to solve a problem the filesystem already solves.

## D005 - No Serilog

**Decision.** A small in-repo structured logger writing JSON lines with size-based rotation
and a redaction pass.

**Why.** The requirement is structured logs with redaction and rotation, which is ~150 lines.
Serilog would add four packages and a configuration surface for the same outcome, and the
redaction pass has to be ours regardless.

## D006 - Shared immutable store, per-instance mutable state

**Decision.** Libraries, assets, client JARs, and runtimes live once in a shared store;
instances reference them. Mods, configs, saves, and logs live in the instance.

**Why.** This gets XMCL's disk-usage win without making instances fragile: repair works per
instance by re-acquiring store files, and no correctness decision depends on hard-link
semantics.

## D007 - Real content services only; no fake providers

**Decision.** Modrinth and CurseForge clients are real implementations against the live APIs.
Where a credential is required (CurseForge), the code is complete and the *live* verification
is marked BLOCKED EXTERNAL rather than replaced by a mock provider in production.

## D008 - No offline/cracked accounts

**Decision.** Microsoft and Yggdrasil-compatible authentication are implemented. Offline
accounts are not implemented at all.

**Why.** The brief forbids cracked-account authentication. The closest legitimate capability
is support for third-party Yggdrasil-compatible auth servers, which is implemented.

## D009 - Forge/NeoForge installers run locally with argument lists

**Decision.** Installer processors are executed through `ProcessStartInfo.ArgumentList` with a
working directory inside the managed store.

**Why.** Faking loader installation by writing metadata would produce instances that do not
launch. Running the official installer locally is how the ecosystem works, and using argument
lists removes any shell-interpretation risk.

## D010 - Argument values are split on spaces except for `-D` properties

**Decision.** A rule-filtered argument value is split into tokens on whitespace, except when the
expanded value starts with `-D`, which is always passed as one argument.

**Why.** Two real metadata shapes conflict. Mojang packs two flags into one value
(`"--width ${resolution_width} --height ${resolution_height}"`), which must become four tokens.
Fabric publishes a single JVM property whose value contains spaces
(`"-DFabricMcEmu= net.minecraft.client.main.Main "`), which must stay one argument. Splitting the
latter shifted the main class, so the JVM launched the vanilla main class and reported
`Completely ignored arguments` for the loader main class and the memory flag. The `-D` rule
fixes that without breaking the multi-flag case, and it also protects Windows paths containing
spaces inside system properties.

## D011 - Java selection prefers the closest supported runtime

**Decision.** For a required Java major version, choose the exact match if present, otherwise the
smallest version above it, and only then anything newer.

**Why.** A "newest compatible" policy looks correct (Java is backward compatible) but breaks
modded play in practice: launching Minecraft 1.21.1 with Fabric 0.19.5 on Java 25 produced a mixin
classloader failure (`MixinExtrasConfigPlugin cannot be cast to IMixinConfigPlugin`) because the
loader's mixin stack does not support that JVM's class file level. With Java 21 the same instance
reached the main menu. Version documents state a required major version; treating it as a target
rather than a floor matches how the ecosystem actually works.

## D012 - Forge/NeoForge installer data resolution follows the installer's own semantics

**Decision.** For each `data` entry: unwrap the per-side object, then treat a string starting with
`/` as an installer entry to extract, a single-element array as a maven coordinate whose path is
`<store>/libraries/<maven path>` (produced by the processor chain, not downloaded), a two-element
array as `[url, sha1]` to download, and anything else as a literal. Processor arguments also
resolve bracketed coordinates such as `[net.neoforged:neoform:...@zip]` to store paths.

**Why.** These shapes were read from a real `neoforge-21.1.251` installer. Guessing produced a
`NullPointerException` inside the official processor, which is a precise signal that the contract
was wrong rather than the loader.

## D013 - xunit v3 with the Microsoft.Testing.Platform runner

**Decision.** Both test projects are xunit v3 executables, and `scripts/test.ps1` runs each project
through its own entry point (`dotnet run --project <project>`).

**Why.** Avalonia's headless xunit integration requires xunit v3, and .NET 10's SDK no longer
supports the VSTest path for Microsoft.Testing.Platform projects. The SDK's `dotnet test`
integration reported `Zero tests ran` for these projects in this SDK build, while the runner's own
entry point discovers and runs them reliably. The script documents that explicitly rather than
leaving a command that silently runs nothing.

## D014 - Interface design language

**Decision.** Graphite neutral surfaces, a copper accent for primary actions and selection, and a
teal tone for success. Navigation is a list, not a row of form controls. Cards are used only for
repeated items and framed panels, never nested.

**Why.** The brief requires an original visual identity and forbids copied assets. The palette is
deliberately two-accent rather than single-hue, and the navigation list keeps the shell reading as
an application rather than a settings form. Both themes were rendered and inspected (V004).

## D015 - One provider contract instead of per-provider installers

**Decision.** `IContentProvider` covers search, project lookup, versions, single-version lookup,
and version selection. `ContentInstaller` depends on that contract, not on `ModrinthClient`, and
`AppServices.InstallerFor` hands the browser the matching installer instance.

**Rejected.** Keeping a Modrinth-shaped installer and adding a parallel CurseForge one. That
would have duplicated dependency resolution, path safety, hash verification, and warning
handling — the parts most likely to rot independently.

**Why.** CurseForge and Modrinth differ in id shape and in the retail-file restriction, not in
what installing content means. Version selection is identical, so it lives once in
`ContentCompatibility`, which also normalises loader spellings (`NeoForge` vs `neoforge`).

## D016 - Provider keys live in the secret store, never in settings

**Decision.** The CurseForge API key is stored through `ProtectedSecretStore` (DPAPI
current-user on Windows) under `config/accounts.bin`, wrapped by `ProviderCredentialStore`.
`LauncherSettings` never contains it, and the UI never reads a stored key back into a field.

**Why.** Settings are a plaintext JSON document that users copy between machines and paste into
bug reports. A key that grants API access does not belong there. The client takes a
`Func<string?>` accessor, so a newly entered key takes effect without rebuilding the client
graph.

## D017 - CurseForge modpack installs resolve files in one bulk request

**Decision.** A `manifest.json` pack resolves every declared `fileID` through
`POST /mods/files` rather than one request per file, and files without a download URL are
reported as warnings.

**Why.** Packs declare dozens to hundreds of files; per-file lookups would be slow and would
multiply rate-limit exposure. The retail restriction is a legitimate limitation of the
distribution terms, so it is surfaced to the user rather than hidden behind a partial install
that silently omits mods.

## D018 - Crash attribution reports evidence, not verdicts

**Decision.** A crash analysis shows what the report says, which installed mods' classes appear in
the stack trace, and which mods the report lists that are no longer installed — as three separate
sections, with an explicit note that a frame proves where code ran, not what caused the crash.

**Rejected.** A single "the crash was caused by X" line. It reads better and would be wrong often:
crash traces pass through framework and mixin code, and the last mod frame is frequently a
victim rather than the cause.

**Why.** This is the same rule the parity matrix uses for itself. A tool that overstates a
diagnosis sends users to the wrong issue tracker and burns their trust the first time it is
wrong. The negative result is reported too: an obfuscated 1.8.8 report matched none of the 48
installed Fabric mods, and the analyzer says so rather than picking the closest name.

## D019 - Network first, cache only on connectivity failures

**Decision.** `CachedContentProvider` serves cached metadata only when the request failed at the
transport level or with 408/429/5xx. A 404, a malformed body, or a rejected API key is returned to
the caller as an error.

**Rejected.** "Serve stale if anything fails" and "cache-first with a TTL". The first hides real
answers, including the difference between "this mod was deleted" and "you are offline". The second
shows old data while the network is fine, which is worse than a short wait for a launcher whose whole
job is to fetch current metadata.

**Why.** The point of the cache is that going offline does not break browsing, not that it becomes
the primary source. The UI states the age of what it is showing, so the user can tell a live answer
from a remembered one.

## D020 - Only launcher-installed content is tracked and updated

**Decision.** Installing content writes a manifest entry linking the file to a provider project and
version. Update checks consider only those entries; a file with no entry is left alone.

**Rejected.** Matching installed jars to projects by file name or by scanning embedded metadata for
an update id. Both produce false matches — two projects ship files with the same name, and a fork
carries the upstream's id — and the failure mode is replacing the wrong mod in someone's pack.

**Why.** Updates are a convenience; silently overwriting the wrong file is data loss. The manifest
also gives the launcher something it otherwise lacks: the knowledge of what it manages, which is
what makes "verify" and "repair" meaningful per file.

## D021 - A versioned build is not offered to an instance without that loader

**Decision.** `ContentCompatibility` refuses a version whose loader list names a mod loader when the
instance declares none. Tokens that mean the base game (`minecraft`, `vanilla`, `datapack`) are not
treated as loader restrictions.

**Why.** A live update run offered a NeoForge Sodium build to a vanilla instance: with no loader on
the instance, the old rule skipped the check entirely and took the newest version regardless of
loader. The install would have succeeded and done nothing, which is the worst kind of failure —
silent. The exception exists because Modrinth labels every resource pack with the `minecraft`
loader, and treating that as a mod loader would refuse all of them on a vanilla instance.

## D022 - A build with no feed key refuses to check for updates

**Decision.** The update feed's public key is embedded in the assembly at build time. If the build
carries no key, `UpdateService` refuses to check and says why, instead of accepting an unsigned or
unverifiable manifest.

**Rejected.** Reading the key from the data directory, and "warn but continue" when no key is
present. The first lets anyone who can write the data directory pair a forged feed with their own
key. The second turns a security property into a dialog nobody reads.

**Why.** The updater downloads an executable and hands it to a script that replaces the installed
program. Every path that reaches that outcome has to be authenticated end to end, and the key is
the only thing that distinguishes the operator's feed from anyone else's.

## D023 - The launcher stages; the user applies

**Decision.** Ferrite verifies and unpacks an update, then prints the command that applies it. It
does not run the hand-off itself, and the script it generates refuses to act on a directory that
does not carry Ferrite's staging marker.

**Rejected.** Restarting into the new build automatically on next launch. That is friendlier, but it
means the launcher replaces its own installation without the user watching, on the strength of a
feed they may never have configured deliberately.

**Why.** A self-update that runs unattended is the largest privilege the product can exercise over
its own machine state. Making the last step explicit, with the exact command shown in Settings,
keeps the user in the loop without making the process manual: the script does the work, including
waiting for Ferrite to exit and cleaning up the staging directory.

## D024 - LAN discovery reads only what the game already broadcasts

**Decision.** Ferrite joins Minecraft's LAN multicast group, parses the announcement, and lists the
world until it stops broadcasting. It never answers a broadcast, never scans a subnet, and never
probes a port it did not read from a datagram.

**Rejected.** Subnet scanning or a port sweep to find worlds. It is faster to write than it sounds,
but it turns "listen to what is being announced on my LAN" into "enumerate the network", which is
something a game launcher has no business doing and which would look exactly like reconnaissance to
anyone watching the network.

**Why.** The datagram is already being sent to every device on the segment; reading it adds nothing
to what the network already exposes. Everything beyond reading it would.

## D025 - Pack compatibility comes from the version's own file, not a table

**Decision.** The format an instance expects is read from `pack_version` in that version's client
jar, and a pack's declared formats are compared against it. When the client file is missing, or
declares a shape this build does not recognise, the result is reported as unknown.

**Rejected.** A hardcoded game-version-to-pack-format table. It is the obvious approach and it is
already stale: a real run reported "no pack format known" for Minecraft 26.3, whose file uses
`resource_major`/`resource_minor` rather than the older `resource`/`data`. A table would have been
wrong for every release after the one it was written from, and wrong silently.

**Why.** The game already states the answer in a file the launcher installs. Reading it costs one
ZIP entry, needs no maintenance, and degrades to "unknown" — which is honest — instead of to a
confident wrong answer.

## D026 - Interface text lives in code tables, and is verified against the views

**Decision.** Two dictionaries (`StringsEn`, `StringsPl`) hold every string the application
displays. `Localizer.Apply` writes the active table into `Application.Resources`, views reference
keys with `DynamicResource`, and view models use `Localizer.Format`. A test parses the views and the
source, compares them with both tables, and fails on a key that is used but undefined or defined but
unused.

**Rejected.** Resource files with satellite assemblies (`.resx`), and translating only the static
labels while leaving messages English.

**Why.** Two languages and one developer do not need a localisation pipeline, and a dictionary is
greppable, diffable, and impossible to ship half-installed. The verification matters more than the
storage format: in Avalonia a missing resource key leaves the property empty, so a typo produces a
blank label and no error. That is exactly the failure this test exists to catch — it already caught
three real ones, including a `Ready` literal hidden in a field initializer.

**Boundary.** Strings produced inside `Ferrite.Core` remain English and are recorded as such in
`FEATURE_PARITY.md` row O10 rather than being presented as translated.

## D027 - OptiFine is recognised and adopted, not reimplemented

**Decision.** Ferrite identifies OptiFine's own installer, runs it with the correct Java in a
directory the launcher owns, and adopts the version it produced into the launcher's store. The
Install click happens in OptiFine's window, and the interface says so.

**Rejected.** Patching a Minecraft version by applying OptiFine's transformations ourselves, and
claiming an "automatic OptiFine install" that silently opens a window the user then has to complete.

**Why.** OptiFine publishes no API, no headless entry point, and no licence allowing a launcher to
redistribute or reimplement its patcher. Reimplementing it would mean chasing a closed, obfuscated
transform for every game version. Running the official installer keeps the licence intact and the
result byte-identical to what a user would get by hand; the honest label keeps the user from
wondering what happened when a window appears.

## D028 - Java precedence, and where a user-supplied path lives

**Decision.** A launch picks its runtime in this order: the runtime pinned on the instance, then the
launcher-wide default, then the best fit for the version's own requirement. Both preferences travel
into the launch as part of `InstanceLaunchRequest` rather than being read from settings inside
`InstanceLauncher`. A path added by hand is stored in settings and re-probed on every scan; a path
that stops answering is dropped.

**Rejected.** Reading settings directly inside the launcher (which would make the launch path
untestable without a settings store), and treating a stored path as trusted forever.

**Why.** The precedence is only correct if all three sources are considered in one place, and the
live run in `docs/VERIFICATION.md` V021.2 pins deliberately the runtime the automatic choice would
not have picked - otherwise a pass would also be explained by the pin being ignored. Passing the
preferences in keeps the rule unit-testable without a JVM on the machine, which is how V021.4 covers
a preference that is no longer installed and an empty catalogue.

**Boundary.** Paths a user adds are probed by executing them, like every other candidate: the probe
is the only gate. An executable that answers like a Java runtime but is not one would still run, and
that is the same trust a user extends by choosing it by hand.
