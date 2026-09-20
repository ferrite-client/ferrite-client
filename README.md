# Ferrite

A native Windows desktop launcher for modded Minecraft, written in C# on .NET 10 with
Avalonia. Ferrite is an original product: its architecture, code, and visual identity are its
own. XMCL is used only as a functional reference for what a current, complete launcher does.

## Status

Under active construction. `FEATURE_PARITY.md` is the authoritative statement of what is
implemented, verified, or externally blocked. Do not trust a summary over that file.

## Requirements

- Windows 11 x64
- .NET SDK 10 (build) or the self-contained package (run)
- Java is not required to be installed: Ferrite can provision a compatible runtime

## Build, test, run

```powershell
dotnet build Ferrite.slnx
dotnet test Ferrite.slnx
dotnet run --project src/Ferrite.App
```

## Data locations

```
%APPDATA%/Ferrite/
  config/     settings, accounts (tokens protected by the OS)
  data/       instances, shared library/asset store, caches, provisioned runtimes
  logs/       structured launcher logs
  tmp/        in-flight downloads
  backups/    backups made before destructive operations
```

The data root can be relocated from Settings.

## Credentials

- **Microsoft sign-in** requires an Azure public-client application ID. Ferrite does not bundle
  one. See `docs/HUMAN_ACTION_REQUIRED.md`.
- **CurseForge** requires a user-issued API key. See `docs/HUMAN_ACTION_REQUIRED.md`.

Neither secret is ever committed to this repository.

## Documentation

| File | Purpose |
| --- | --- |
| `FEATURE_PARITY.md` | Capability matrix against XMCL, with honest status |
| `docs/RESEARCH.md` | Primary-source research and its implications |
| `docs/ARCHITECTURE.md` | Module map, storage layout, pipelines |
| `docs/IMPLEMENTATION_PLAN.md` | Milestones and exit criteria |
| `docs/DECISIONS.md` | Decisions with rejected alternatives |
| `docs/SECURITY.md` | Threat model and security controls |
| `docs/VERIFICATION.md` | Recorded evidence for verified capabilities |
| `docs/DEVELOPMENT.md` | Build, test, package, conventions |
| `docs/HUMAN_ACTION_REQUIRED.md` | Externally blocked items and exact user steps |
