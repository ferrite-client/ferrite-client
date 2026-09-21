# Development

## Requirements

- Windows 11 x64 (primary target)
- .NET SDK 10.0 (10.0.201 verified)
- Git

## Build

```powershell
./scripts/build.ps1
```

or directly:

```powershell
dotnet build Ferrite.slnx -c Debug
```

## Test

```powershell
./scripts/test.ps1
```

or directly:

```powershell
dotnet test Ferrite.slnx -c Debug
```

## Run

```powershell
dotnet run --project src/Ferrite.App
```

## Package

```powershell
./scripts/package.ps1
```

Produces both Windows x64 shapes under `artifacts/`, each with a zip beside it:

| Shape | Size | Needs .NET installed |
| --- | --- | --- |
| `framework-dependent` | ~31 MiB, ~13 MiB zipped | Yes, the .NET 10 desktop runtime |
| `self-contained` | ~107 MiB, ~48 MiB zipped | No |

Options: `-Only framework-dependent|self-contained`, `-Runtime win-arm64`, `-Version 0.2.0`,
`-NoArchive`, `-OutputDirectory <path>`. The script refuses to write outside the repository and
deletes the output directory it is about to fill, so a package never mixes builds.

Notes that matter for a release:

- Avalonia's SkiaSharp/HarfBuzzSharp native symbol files are dropped from the publish output; they
  are tens of megabytes and of no use to a user. Managed symbols are embedded in the assemblies
  instead, so exceptions in a user's diagnostics bundle still carry line numbers.
- Neither shape is code-signed. Unsigned builds show a SmartScreen warning on first run; see
  `docs/HUMAN_ACTION_REQUIRED.md` for the certificate that would be needed.

## Conventions

- Nullable reference types are enabled and warnings are errors. A build that produces a warning
  is a failing build.
- Code is organised by folder/namespace under `Ferrite.Core`; UI code never contains domain
  logic.
- Long operations take a `CancellationToken` and never run on the UI thread.
- New behaviour that touches untrusted input needs a test that exercises hostile input.
- `FEATURE_PARITY.md` and the relevant document under `docs/` are updated with each milestone.

## Repository layout

```
src/Ferrite.Core          domain, services, no UI
src/Ferrite.App           Avalonia desktop application
tests/Ferrite.Core.Tests  unit and integration tests (xunit v3)
tests/Ferrite.App.Tests   headless UI rendering tests (Avalonia headless + xunit v3)
tools/Ferrite.Verify      live verification harness against real services
docs/                     architecture, research, plan, decisions, security, verification
scripts/                  build, test, verify-live, parity summary
```

## Test platform note

.NET 10 no longer supports the VSTest path for Microsoft.Testing.Platform projects, and the SDK's
`dotnet test` integration did not discover xunit v3 tests here (it reported "Zero tests ran"). Use
`scripts/test.ps1`, which runs each test project through its own runner entry point. The UI tests
write frames to `$env:FERRITE_UI_SHOTS` when that variable is set.

## Live verification

`scripts/verify-live.ps1` runs real workflows against Mojang and Modrinth:

```powershell
./scripts/verify-live.ps1 java
./scripts/verify-live.ps1 manifest
./scripts/verify-live.ps1 install 26.3
./scripts/verify-live.ps1 launch 26.3 --seconds 60
./scripts/verify-live.ps1 fabric 1.21.1
./scripts/verify-live.ps1 neoforge 1.21.1
./scripts/verify-live.ps1 content 1.21.1 --slug sodium
./scripts/verify-live.ps1 content 1.21.1 --slug sodium --version <id> --loader fabric
./scripts/verify-live.ps1 updates --instance <name> --apply
./scripts/verify-live.ps1 modpack <pack.mrpack or url>
./scripts/verify-live.ps1 worlds --game-dir %APPDATA%\.minecraft
./scripts/verify-live.ps1 ping mc.hypixel.net
./scripts/verify-live.ps1 crash
./scripts/verify-live.ps1 diagnostics --out <path>.zip
./scripts/verify-live.ps1 cache sodium
./scripts/verify-live.ps1 curseforge jei          # reports the blocked state without a key
./scripts/verify-live.ps1 sign-update --version 2.0.0 --package <zip> --out <feed>
./scripts/verify-live.ps1 update-check --feed <feed> --key <feed>/update-public-key.pem --current 0.1.0 --stage
```

`update-check` accepts a local directory as `--feed`, which it serves over loopback HTTP, so a feed
can be exercised before it is hosted. Results are recorded in `docs/VERIFICATION.md`.
