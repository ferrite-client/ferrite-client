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

---

## Audit log

### 2026-09-21 - implementation audit

Every path that consumes untrusted input was re-read after the feature work, with these results.

| Area | Finding |
| --- | --- |
| Archive extraction | Entry names are normalised and containment-checked; absolute paths, traversal segments, drive qualifiers, reserved device names, and symlinks are rejected or skipped. Entry count and expanded size are bounded. Covered by tests. |
| Archive metadata reads | **Fixed.** `ReadEntryText` and the mod scanner trusted the archive header's declared entry length before reading. The declared length is attacker-controlled, so both now bound the decompressed stream itself and throw `PathSafetyException` past the cap. A regression test covers it. |
| Zip bomb in mod metadata | Covered by the fix above: a mod whose metadata entry streams more than 4 MiB is treated as unreadable rather than buffered. |
| Path construction | Instance-scoped writes go through containment checks in the content installer and the virtual-asset mirror. Runtime entry paths and installer data entries are containment-checked or sanitised. |
| Remote file names | Provider file names pass through `PathSafety.SanitizeFileName` before they become paths. |
| Process execution | Java, installer processors, and shell-open all use `ProcessStartInfo.ArgumentList`. No shell string is constructed anywhere in the codebase. |
| Command arguments | Launch argument substitution fails closed on an unknown placeholder, and processor substitution resolves bracketed maven coordinates to store paths rather than passing them through. |
| Credential persistence | Tokens live in the DPAPI-protected secret store, keyed per account. Non-Windows builds report degraded protection in the UI. |
| Credential disclosure | The central redactor is applied to every log line and to the launch-command preview; both are covered by tests. Third-party logging (NeoForge's ModLauncher) was observed redacting tokens itself, which is why the launcher must not rely on it. |
| Temporary files | Downloads write to a `.part` sibling and are moved into place only after verification. Installer working directories are deleted in a `finally` block. |
| HTTP handling | HTTPS everywhere, bounded response sizes, per-attempt timeouts, retry classification, and no plaintext endpoints in the default configuration. |
| Update installation | Manifests are fetched over TLS and accepted only when their detached signature verifies against a key embedded in this build (ECDSA P-256 or RSA, SHA-256); the package's SHA-256 is checked before it is unpacked, extraction uses the same containment-checked extractor as every other archive, and staging happens outside the install root. The launcher never replaces its own running files: it writes a hand-off script whose body is a constant and which receives every path as a named argument, so no value from the feed is interpolated into script text. Covered by tests. |

**Residual risks accepted for now**

- Mod JARs are parsed as ZIP archives and their metadata is read; the JAR's own bytecode is never
  loaded or executed by the launcher.
- The launcher runs with user privileges. A defect in a path check would be a file-write primitive,
  which is why containment is centralised rather than re-implemented per call site.
