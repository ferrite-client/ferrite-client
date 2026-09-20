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
src/Ferrite.Core   domain, services, no UI
src/Ferrite.App    Avalonia desktop application
tests/             automated tests
docs/              architecture, research, plan, decisions, security, verification
scripts/           build, test, package, parity summary
```
