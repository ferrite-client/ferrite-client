# Research Notes

Sources consulted while building Ferrite: what each source describes, the assumptions we
made, and how the finding affects implementation. `VERIFIED` = fetched or exercised live
during development. `ASSUMED` = design assumption not yet confirmed. `LIMIT` = external
restriction.

---

## 1. Functional reference: X Minecraft Launcher (XMCL)

Sources: https://github.com/Voxelum/x-minecraft-launcher (README, fetched 2026-09-20),
https://xmcl.app.

**VERIFIED** XMCL is an Electron-based cross-platform launcher whose headline capabilities are:

- Download and auto-complete Minecraft, Forge, Fabric, Quilt, OptiFine, and JVM from official
  or third-party mirrors.
- Multi-part concurrent downloads reusing HTTP/HTTPS sockets.
- Multi-instancing: separate instances isolate versions, mods, and launch settings.
- Resource installation via hard/symbolic links so resources are not duplicated per instance.
- Built-in CurseForge and Modrinth browsing and downloads.
- Modpack import/export compliant with CurseForge and Modrinth formats.
- Multiple account systems: Microsoft (OAuth), Mojang Yggdrasil, ely.by, littleskin.cn, plus
  user-supplied third-party authentication servers.
- Peer-to-peer LAN play between users not on the same physical LAN.
- Code-signed Windows packaging via appx/appinstaller.

Its published core packages reveal the feature surface a full launcher needs: core launch,
installer (Minecraft/Forge/Fabric/Quilt/OptiFine/JVM), user auth and skin, mod parsing
(Forge/LiteLoader/Fabric), NBT, game data (level data, servers.dat), resource packs, game
settings, server ping client, model rendering, text components, Forge site parser, file
transfer, and UPnP/NAT-PMP port mapping.

**Implications.** Parity target is broad: instances, content, modpacks, accounts, Java,
loaders, worlds, servers, diagnostics, packaging. Two XMCL capabilities are deliberately not
reproduced:

- **Offline/cracked accounts** are forbidden by the product brief. Third-party
  Yggdrasil-*compatible* authentication servers are legitimate and are implemented; offline
  accounts are not. LIMIT.
- **Peer-to-peer relay play** needs a third-party relay service we do not operate. See section 9.

XMCL is a functional reference only. No XMCL source, branding, or assets are copied.

---

## 2. Mojang launcher metadata

Source: https://piston-meta.mojang.com/mc/game/version_manifest_v2.json (**VERIFIED**,
2026-09-20).

- Shape: `{ "latest": { "release", "snapshot" }, "versions": [ { id, type, url, time,
  releaseTime, sha1, complianceLevel } ] }`.
- 915 versions published. `type` is `release`, `snapshot`, `old_beta`, or `old_alpha`.
- Newest release on 2026-09-20 is `26.3`. Minecraft now uses a year-stream scheme
  (`26.3`, `26.3-rc-3`, `26.3-snapshot-10`), so version ordering must be metadata driven,
  never string comparison.

Source: version document for `26.3`
(`https://piston-meta.mojang.com/v1/packages/96c00d95a31328714d3811cfade2804bb050e455/26.3.json`)
(**VERIFIED**).

- Top-level: `id`, `type`, `releaseTime`, `time`, `complianceLevel`, `minimumLauncherVersion`
  (= 21), `mainClass` (`net.minecraft.client.main.Main`), `assets` (`"34"`), `assetIndex`
  (`{ id, sha1, size, totalSize, url }`), `javaVersion` (`{ component:
  "java-runtime-epsilon", majorVersion: 25 }`), `logging.client` (`{ argument:
  "-Dlog4j.configurationFile=${path}", file: { id, sha1, size, url }, type }`).
- `downloads.client` / `downloads.server` carry `{ sha1, size, url }`. The 26.3 client JAR is
  41.4 MB on piston-data.mojang.com.
- `libraries[]`: `name` (maven coordinates), optional `rules`, and `downloads.artifact`
  which in 26.3 **includes an explicit `path`** besides `sha1`/`size`/`url`. Older versions
  omit `path`, so the path must be derivable from the maven name when absent. Libraries may
  also carry `downloads.classifiers` and a `natives` map for native extraction.
- `arguments.game[]` and `arguments.jvm[]` mix plain strings with rule objects
  `{ rules: [...], value: string | string[] }`. Observed 26.3 game arguments include
  `--username ${auth_player_name}`, `--version ${version_name}`, `--gameDir
  ${game_directory}`, `--assetsDir ${assets_root}`, `--assetIndex ${assets_index_name}`,
  `--uuid`, `--accessToken`, **`--clientId ${clientid}`**, `--xuid ${auth_xuid}`,
  `--versionType`, `--width`/`--height`, and the `--quickPlay*` family
  (`--quickPlayPath`, `--quickPlaySingleplayer`, `--quickPlayMultiplayer`,
  `--quickPlayRealms`).
- **New in this generation:** `arguments.default-user-jvm[]`. A rule-filtered list that
  supplies default user-overridable JVM settings. For 26.3 it contains `-Xms2G -Xmx4G
  -XX:+UseCompactObjectHeaders -XX:+AlwaysPreTouch -XX:+UseStringDeduplication`, plus a ZGC
  block allowed on osx/linux/windows >= 10.0.17134 and a G1 block for older Windows. A
  launcher must not apply these as unconditional defaults when the user set memory explicitly.
- Rule shapes observed live: `{ "action": "allow", "os": { "name": "windows",
  "versionRange": { "min": "10.0.17134" } } }` and feature rules
  `{ "action": "allow", "features": { "is_demo_user": true } }`.
- Maven coordinates map to `group/with/slashes/artifact/version/artifact-version[-classifier].jar`
  served from `https://libraries.minecraft.net/`.
- Client JAR: `https://piston-data.mojang.com/v1/objects/<sha1>/client.jar`.
- Asset index objects: `{ "objects": { "<name>": { "hash", "size" } } }`; files live at
  `<assets_root>/objects/<first2>/<hash>`, with a virtual copy at
  `<assets_root>/virtual/legacy/<name>` for pre-1.7 indexes.

**Implications.** The installer must resolve version inheritance, evaluate `rules` against
OS/arch/features, derive library paths from explicit `path` or maven coordinates, extract
natives honouring `exclude`, verify SHA-1, and honour `complianceLevel`.

---

## 3. Java runtimes

Source: https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json
(**VERIFIED**, 13,385 bytes).

- Shape: `{ "<os>": { "<component>": [ { manifest: { sha1, size, url }, version: { name,
  released }, availability: { group, progress } } ] } }`. OS keys: `gamecore`, `windows`,
  `linux`, `macos`. Components: `jre-legacy`, `java-runtime-alpha`, `java-runtime-beta`,
  `java-runtime-gamma`, `java-runtime-gamma-snapshot`, `java-runtime-delta`,
  `java-runtime-epsilon`, `minecraft-java-exe`.
- Each entry points at a `manifest.json` listing every file with `{ type:
  "file"|"directory"|"link", downloads: { raw, lzma }, executable }`.
- At fetch time every `gamecore` component array was empty while `windows` carried real
  entries, so provisioning must handle "no runtime published" explicitly.
- Minecraft 26.3 requires `javaVersion.majorVersion` 25.

**Implications.** Provisioning reads this manifest, picks OS + component, downloads the JRE
file manifest, and materialises the runtime under our runtime store. `raw` URLs are always
available, so `raw` is the correct simple path; `lzma` entries are not required.

---

## 4. Mod loaders

### Fabric — **VERIFIED**

Sources: https://meta.fabricmc.net/v2/versions/game, `/v2/versions/loader`,
`/v2/versions/loader/{game}/{loader}`, `/v2/versions/loader/{game}/{loader}/profile/json`.

- Loader entries: `{ separator, build, maven, version, stable }`, queried per game version;
  the `intermediary` entry is keyed by the Minecraft version itself.
- The profile JSON is a version-document fragment: `id` (`fabric-loader-0.19.5-1.21.1`),
  `inheritsFrom` (base Minecraft version), `mainClass`
  (`net.fabricmc.loader.impl.launch.knot.KnotClient`), `arguments.jvm`
  (`-DFabricMcEmu= net.minecraft.client.main.Main `), and plain `libraries[]` of the form
  `{ name, url, md5, sha1, sha256, sha512, size }`.
- fabric-loader `0.19.5` and asm `9.10.1` were current at fetch time.

### Quilt — **VERIFIED**

Source: https://meta.quiltmc.org/v3/versions/loader and
`/v3/versions/loader/{game}/{loader}/profile/json`.

- Same shape as Fabric: `{ maven, version, build, separator, file_size, hashes: { sha1,
  sha256 } }`.
- Profile carries `inheritsFrom`, `mainClass`
  (`org.quiltmc.loader.impl.launch.knot.KnotClient`), and `libraries[]` that may list only
  `{ name, url }` **without hashes**, so maven resolution must tolerate unverified downloads
  or fetch the neighbouring `.sha1`.

### NeoForge / Forge — **VERIFIED (metadata only)**

Source: https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml.

- Versions follow `<minecraft-minor>.<build>.<patch>[-beta]`, e.g. `26.3.0.7-beta`,
  `20.2.12-beta`. Installer JARs are published on the same host. Installing one requires
  running the installer's `install_profile.json` processor chain with a working JVM inside
  the installer's own working directory.

**VERIFIED** against `neoforge-21.1.251-installer.jar` (7.0 MB, fetched 2026-09-21):

- `install_profile.json` keys: `spec`, `profile`, `version`, `icon`, `minecraft`, `json`, `logo`,
  `welcome`, `mirrorList`, `hideExtract`, `data`, `processors`, `libraries`, `serverJarPath`.
- **Correction to the initial assumption:** `json` holds a *jar-relative path* to the version
  document (`/version.json`), not inline JSON, and `path` is empty in this generation. A resolver
  must detect the shape: a value starting with `{` is inline, anything else is an entry name.
- `data` keys for a 1.21.1 client install: `MAPPINGS`, `MOJMAPS`, `MERGED_MAPPINGS`, `BINPATCH`,
  `MC_UNPACKED`, `MC_SLIM`, `MC_EXTRA`, `MC_SRG`, `PATCHED`, `MCP_VERSION`. Each value is either a
  two-element `[url, sha1]` array to download or a literal string; a string beginning with `/` is
  an entry inside the installer jar.
- 10 processors are declared. Each has `jar`, `classpath[]`, `args[]`, optional `sides[]`, and
  optional `outputs`. A processor declaring `sides: ["server"]` is skipped for a client install.
  Arguments use `{INSTALLER}`, `{ROOT}`, `{SIDE}`, `{MINECRAFT_JAR}` and data keys such as
  `{MC_SRG}`, `{PATCHED}`, `{BINPATCH}`.
- `{ROOT}` must be the directory whose `libraries` child is the library store, because a processor
  writes `--to {ROOT}/libraries/net/neoforged/neoforge/<version>/win_args.txt`. Ferrite therefore
  passes its shared store directory as `{ROOT}` so artefacts land where the launcher looks for them.
- The processor main class comes from the processor jar's `META-INF/MANIFEST.MF` `Main-Class`.

**Implications.** Fabric and Quilt are pure metadata merges and can be verified end to end
locally. Forge and NeoForge depend on running the official installer processors. That is
legitimate, but it must be invoked with an argument list, never a shell string, and its
working directory must stay inside the instance and library store.

---

## 5. Content services

### Modrinth — **VERIFIED**

Sources: https://api.modrinth.com/v2/... fetched live 2026-09-20.

- `GET /v2/search?query=&limit=&offset=&facets=[[...]]` returns `hits[]` with `project_id`,
  `slug`, `title`, `description`, `project_type`, `downloads`, `icon_url`, `author`,
  `categories`, `versions`, `date_modified`.
- `GET /v2/project/{id|slug}` returns a full project. `GET /v2/project/{id}/version` accepts
  `loaders=[...]` and `game_versions=[...]` JSON-encoded filters. Versions expose `id`,
  `version_number`, `version_type`, `game_versions`, `loaders`, `dependencies[]`
  (`{ project_id, version_id, dependency_type }`), and `files[]` with `url`, `filename`,
  `primary`, `size`, `hashes.sha1`, `hashes.sha512`.
- `GET /v2/tag/game_version`, `/v2/tag/loader`, `/v2/tag/category`, `/v2/tag/project_type`
  provide filter vocabularies.
- Observed: project `sodium` (`AANobbMI`) has 228M downloads; its Fabric 1.21.1 version
  `SMxNOGZ6` ships `sodium-fabric-0.8.13+mc1.21.1.jar`, size 1,574,609, SHA-1
  `003c114c85ca88ef3362e018deb6aca0c682d6a1` plus SHA-512.

### CurseForge — **IMPLEMENTED (live calls BLOCKED EXTERNAL)**

Source: the published CurseForge "Eternal" API (v1) documentation. The service requires an
`x-api-key` header on every request; keys are issued through the CurseForge developer console
and their terms restrict redistribution of files whose authors disabled third-party
distribution.

Endpoints Ferrite uses (`https://api.curseforge.com/v1`):

- `GET /mods/search` — parameters `gameId` (Minecraft is `432`), `searchFilter`, `index`,
  `pageSize` (max 50), `classId`, `gameVersion`, `modLoaderType`, `sortField`, `sortOrder`.
  Response carries `data[]` plus `pagination.totalCount`.
- `GET /mods/{modId}` — one project. There is no slug lookup, so a slug is resolved through an
  exact-term search first.
- `GET /mods/{modId}/files` — files of a project, filtered by `gameVersion` and
  `modLoaderType`.
- `GET /mods/{modId}/files/{fileId}` — one file, used when a specific version is pinned.
- `POST /mods/files` with `{"fileIds":[...]}` — bulk file metadata. This is what makes a
  modpack install practical: a manifest lists dozens of file ids and one request resolves them.
- `GET /mods/{modId}/files/{fileId}/download-url` — the file's URL, or `null` when the author
  disallowed third-party distribution. Ferrite treats that as a reported warning, never as a
  silent skip.

Numeric identifiers (`CurseForgeIds`):

- `classId`: mod `6`, modpack `4471`, resource pack `12`, shader `6552`, datapack `6945`.
- `modLoaderType`: forge `1`, fabric `4`, quilt `5`, neoforge `6`.
- `releaseType`: release `1`, beta `2`, alpha `3`.
- dependency `relationType`: embedded `1`, optional `2`, required `3`, tool `4`,
  incompatible `5`.

A file's `gameVersions` array mixes game versions and loader names (for example
`["1.21.1", "NeoForge", "Client"]`), so Ferrite splits it into game versions and loaders before
compatibility matching.

**CurseForge modpack format.** A ZIP whose root contains `manifest.json` plus an overrides
folder (usually `overrides`, named by the manifest's `overrides` field). The manifest declares
`minecraft.version`, `minecraft.modLoaders[]` (`{"id":"neoforge-21.1.72","primary":true}`),
`files[]` (`projectID`, `fileID`, `required`), `name`, `version`, and `manifestVersion`. The
loader id is `<loader>-<version>`. Pack files land in `mods/`; everything else ships inside
the overrides tree.

**Limits.** Without a key, every call fails by design and the integration reports a
configuration state instead of pretending to work. A file with no download URL cannot be
installed at all: only the official launcher may fetch it. Both are recorded in
`docs/HUMAN_ACTION_REQUIRED.md` (H2) and `FEATURE_PARITY.md` (K05).

### Modrinth modpack format (.mrpack) — **VERIFIED**

Read from `Fabulously.Optimized-v6.5.0.mrpack` (177,589 bytes, fetched 2026-09-21):

- A ZIP whose first entries are `overrides/` and `modrinth.index.json` (65 entries in this pack).
- `modrinth.index.json`: `formatVersion` (=1), `game` (`"minecraft"`), `versionId` (`"6.5.0"`),
  `name`, `dependencies`, and `files[]`.
- `dependencies` is a map of loader id to version, for example
  `{"fabric-loader": "0.19.3", "minecraft": "1.21.1"}`. The keys to expect are `minecraft`,
  `fabric-loader`, `quilt-loader`, `forge`, and `neoforge`.
- Each `files[]` entry: `path` (instance-relative, for example
  `mods/BetterGrassify-1.8.6+fabric.1.21.1.jar`), `hashes.sha1`, `hashes.sha512`, `env.client`,
  `env.server` (`required` / `optional` / `unsupported`), `downloads[]` (URL list), and `fileSize`.
- 50 declared files in this pack, so a modpack install is mostly a hashed download pass plus an
  overrides extraction.
- `overrides/` mirrors the game directory; `client-overrides/` is the client-only variant.

---

## 6. Authentication chain

Endpoints probed live 2026-09-20 (**VERIFIED alive**; 400/401 on malformed requests proves
reachability, not failure):

- `https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode` -> 400
- `https://user.auth.xboxlive.com/user/authenticate` -> transport error on a bodyless POST
- `https://xsts.auth.xboxlive.com/xsts/authorize` -> 400
- `https://api.minecraftservices.com/authentication/login_with_xbox` -> 400
- `https://api.minecraftservices.com/minecraft/profile` -> 401

Documented chain Ferrite implements:

1. Microsoft OAuth 2.0, device code flow (native app) or authorization code with PKCE.
2. `user.auth.xboxlive.com/user/authenticate` with the Microsoft access token -> Xbox Live
   user token.
3. `xsts.auth.xboxlive.com/xsts/authorize` with relying party
   `rp://api.minecraftservices.com/` -> XSTS token, plus the well-known XErr codes when an
   account has no Xbox profile or is a child account.
4. `api.minecraftservices.com/authentication/login_with_xbox` -> Minecraft access token.
5. `api.minecraftservices.com/entitlements/mcstore` -> ownership check.
6. `api.minecraftservices.com/minecraft/profile` -> UUID, name, skins, capes.

**LIMIT.** Ferrite ships with no bundled Microsoft application client ID. Users register
their own Azure public client (personal accounts, no secret) or supply a client ID in
settings. Final live sign-in is therefore BLOCKED EXTERNAL while everything up to the network
boundary is implemented and unit tested.

**VERIFIED** from 26.3 game arguments: the modern client expects `--clientId` and `--xuid`,
so the account record retains the Xbox XUID alongside the Minecraft access token.

---

## 7. Server status protocol

- The legacy list ping (`0xFE 0x01`) and the modern handshake/status exchange (`0x00` handshake
  -> `0x00` status request -> JSON response) are documented public protocol behaviour.
  Ferrite implements the modern protocol with VarInt framing and bounded response size, plus a
  legacy fallback.

**VERIFIED** live on 2026-09-21:

- `play.cubecraft.net` answered the modern status exchange with
  `CubeCraft`, `757/5000` players and a 126 ms round trip.
- `mc.hypixel.net` answered with `Requires MC 1.8 / 1.21`, `23644/200000` players, 406 ms, and a
  multi-line MOTD (`Hypixel Network [1.8/26.3]` plus a second line).
- Unreachable hosts (`2b2t.org` timing out, an unknown hostname) produce an offline status with the
  reason instead of an exception.

The response's `description` arrives either as a plain string or as a chat component with nested
`extra` arrays and legacy section-sign colour codes; both shapes are flattened to plain text.

## 8. NBT

- `level.dat` is gzip-compressed NBT; `servers.dat` is uncompressed NBT. Ferrite implements a
  focused read-only NBT parser sufficient for world metadata and server lists, with strict
  bounds checking, instead of a general NBT library.

## 9. Peer-to-peer LAN relay — LIMIT

- **Local discovery (**VERIFIED**, V013):** Minecraft publishes worlds opened to LAN as a UDP
  multicast datagram to 224.0.2.60:4445 with the payload `[MOTD]<name>[/MOTD][AD]<port>[/AD]`. That
  address is in the local network control block (224.0.0.0/24), which routers deliberately do not
  forward, so a listener on it only sees its own network. Ferrite joins the group, parses the
  payload, and lists a world until 15 seconds after its last broadcast.
- XMCL's "play over the internet as if on LAN" is a different capability: it depends on a
  relay/rendezvous service Ferrite does not operate and will not reuse. That part stays externally
  restricted (M10, `HUMAN_ACTION_REQUIRED.md` H4).

## 10. Launcher self-update — LIMIT

- Best practice: check a signed manifest over TLS, download the new package into a staging
  directory outside the install root, verify a hash, and hand off to an OS installer instead of
  overwriting a running binary. XMCL signs its Windows packages with appx/appinstaller, which needs
  a certificate; Ferrite takes the portable route of a detached signature over the manifest plus a
  hand-off script.
- **Implemented (**VERIFIED**, V012):** `manifest.json` + `manifest.json.sig` fetched over TLS (or
  loopback HTTP for a local feed), ECDSA P-256 or RSA detached signature verified over the exact
  manifest bytes before it is parsed, dotted-version comparison, SHA-256 and size verification of
  the package, unpacking into `<data root>/staging/<version>/payload` with the traversal-safe
  extractor, and a generated hand-off script that waits for the launcher to exit, replaces the
  install directory, starts the new build, and removes the staging directory and itself.
- A feed names its packages by file name relative to the feed root, which keeps one signed manifest
  valid on any host; absolute HTTPS URLs are also accepted. Package URLs may not use `file:`,
  `ftp:`, an absolute path, or `..`.
- **Remaining limit:** hosting the feed and the built packages. Nothing about that is code, so it
  stays BLOCKED EXTERNAL (H3). The publisher tooling is `Ferrite.Verify sign-update`.
