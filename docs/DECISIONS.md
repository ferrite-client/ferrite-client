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
