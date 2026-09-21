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

### 2026-09-21 - second audit: the surfaces added since the first

The features added after the first audit - Feed The Beast packs, LabyMod, the local server, the world
map and chunk editor, and the structure preview - were reviewed the same way: every value that
arrives from outside is traced to the place it is used.

| Area | Finding |
| --- | --- |
| FTB pack file paths | A pack's `path` and file name come from FTB's document. They are normalised and containment-checked before becoming a download target, a refused entry is reported rather than skipped, and an absolute path is refused outright. A defect was found and fixed here: the first version stripped `./` from *every* position instead of only the front, which would have rewritten `../../../x` into a harmless path instead of refusing it. Covered by tests. |
| FTB pack file downloads | Each file is fetched with its published SHA-1 when it has one, and the launcher's own HTTPS endpoints are unchanged. FTB's files are the pack's own content, so they carry the same trust as any downloaded mod. |
| LabyMod manifest, libraries, and version document | Fetched over TLS from LabyMod's published host, bounded by size, and parsed into typed models. The merged version profile names libraries by maven coordinates, which the launcher turns into store paths itself rather than trusting a path from the document. |
| LabyMod version id | **Fixed.** The manifest's `commitReference` became part of the version id, which becomes a directory name under the store - a remote value steering a filesystem path. It is now checked against an identifier rule (letters, digits, dot, underscore, hyphen, no traversal segment) and refused otherwise, before anything is written. The requested Minecraft version is checked the same way. Covered by tests, including a manifest whose commit is `../../escape`. |
| LabyMod assets | Asset names are sanitised and containment-checked before they become file names. The manifest publishes no checksum for the asset files themselves, so they are stored unverified; that limitation is recorded in `docs/VERIFICATION.md` V042.4 rather than hidden. |
| Local server files | `server.properties` is merged rather than rewritten, so a key the user owns is preserved; the file lives in the instance's own `server` directory. The command is an argument list through the same process API as a game launch. |
| Local server jar | Downloaded from the version document's own `downloads.server` and verified against its SHA-1 before use. |
| Region file reads and writes | Paths are computed from a world directory the caller supplies plus integer chunk coordinates; nothing from a world file steers a path. Every region file a rewrite touches is copied into the launcher's backups first, and the world's folder name is sanitised before it is used as a backup directory name. |
| Chunk payloads | Copied through the bounds-checked NBT reader and writer. A chunk whose NBT cannot be read is skipped rather than written back, so a corrupt chunk cannot be replaced with a truncated one. |
| Structure files | Parsed through the same bounded NBT reader (depth 64, collection 4 MiB, array 64 MiB). The declared size is used for display only and never allocates, and a file that declares no size is refused. |
| Client data version reads | The `version.json` entry inside a client jar is size-capped before it is parsed, and an unreadable or absent value is reported as "cannot be checked" rather than guessed. |
