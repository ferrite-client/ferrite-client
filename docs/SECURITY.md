# Security

This document describes how Ferrite handles hostile input. It is maintained alongside the code
and re-checked during the final security audit.

## Threat model

Everything outside the process is untrusted: HTTP responses, JSON/TOML/XML metadata, archive
entries, archive filenames, mod JARs, world data, server responses, and user-supplied paths and
URLs. The launcher runs with the user's privileges, so a path-escape bug is a file-write
primitive and a process-execution bug is code execution.

## Credential protection

- Microsoft access, refresh, and XSTS tokens are never written to logs, crash output, or the
  diagnostics bundle. Redaction is applied centrally in the logger and again when rendering a
  launch-command preview.
- Persisted token material is encrypted with the Windows Data Protection API (per-user scope)
  through `ProtectedData`. Non-Windows builds store tokens in a file with user-only
  permissions and mark the storage as degraded in the UI.
- The launcher never asks for, receives, or stores a Microsoft account password. Sign-in uses
  OAuth device code or authorization code with PKCE only.

## Archive extraction

- Every entry name is normalised and canonicalised, then checked for containment inside the
  intended destination. Entries that are absolute, contain `..` segments, contain a drive
  designator, contain a null byte, or resolve outside the destination are rejected.
- Symbolic links and hard links inside archives are not created.
- Entry count and total uncompressed size are capped to prevent archive bombs.
- Extraction writes into a temporary directory first and is finalised by moving into place, so
  a failed extraction cannot leave a partially-populated destination.

## Path safety

- All instance-scoped writes go through a resolver that canonicalises the target and refuses
  paths that leave the instance root.
- Filenames derived from remote metadata are sanitised; invalid characters and reserved device
  names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`) are rejected or rewritten.
- Paths are never concatenated from untrusted strings without the containment check.

## Process and command safety

- Java and installer processes are started with `ProcessStartInfo.ArgumentList`. No shell
  string is ever constructed, so shell metacharacters in arguments have no effect.
- The executable path for a process is validated to exist and, for provisioned runtimes, to
  live inside the managed runtime store.
- Launch argument substitution fails closed: an unknown `${...}` placeholder aborts the launch
  instead of silently producing a wrong argument.

## Network handling

- All endpoints are HTTPS. Plain HTTP is only reachable through an explicitly configured
  mirror, and the UI marks such a mirror as insecure.
- Responses are size-capped before parsing, and JSON/TOML parsing errors surface as typed
  failures rather than crashes.
- Redirects are followed but the final host is checked against the expected host allow-list for
  artefact downloads.

## Updater

- Update manifests are fetched over TLS and are only trusted when their signature or hash
  matches a value embedded in the installed build.
- Packages are staged outside the installation directory, verified, and handed to an installer;
  a running binary is never overwritten in place.

## Temporary files and backups

- Temporary files live in `<data root>/tmp` with unpredictable names and are cleaned at
  startup.
- Backups made before destructive operations are written under `<data root>/backups` and are
  never deleted automatically while they are the only copy of user data.

## Reporting

Security-relevant behaviour changes must update this document and add a regression test.
