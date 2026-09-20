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

Produces a self-contained Windows x64 build under `artifacts/`.

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
```

Results are recorded in `docs/VERIFICATION.md`.
