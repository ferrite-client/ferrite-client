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

### CurseForge — **LIMIT**

- The official API requires a key issued through the CurseForge developer console, and its
  terms restrict file redistribution. Ferrite implements the full client (search, project,
  files, dependency resolution, modpack install) but live calls require a user-supplied API
  key; without one the integration reports a clear configuration state.
- CurseForge modpacks are ZIP archives with `manifest.json` plus `overrides/`; the manifest
  lists `files[]` with `projectID`, `fileID`, and `required` flags that only the API can
  resolve.

### Modrinth modpack format (.mrpack) — **ASSUMED**

- Expected: ZIP containing `modrinth.index.json` with `formatVersion`, `game`, `versionId`,
  `name`, `files[]` (`{ path, hashes: { sha1, sha512 }, env, downloads[], fileSize }`),
  `dependencies` (`{ minecraft, fabric-loader | forge | neoforge | quilt-loader }`), plus
  `overrides/` and optional `client-overrides/`.
- To be confirmed against a live `.mrpack` during verification.

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

## 8. NBT

- `level.dat` is gzip-compressed NBT; `servers.dat` is uncompressed NBT. Ferrite implements a
  focused read-only NBT parser sufficient for world metadata and server lists, with strict
  bounds checking, instead of a general NBT library.

## 9. Peer-to-peer LAN relay — LIMIT

- XMCL's "play over the internet as if on LAN" depends on a relay/rendezvous service Ferrite
  does not operate and cannot reuse. Ferrite implements local LAN announcement assistance
  only and documents the relay capability as externally restricted.

## 10. Launcher self-update — LIMIT

- Best practice: check a signed manifest over TLS, download the new package into a staging
  directory outside the install root, verify a hash, and hand off to an OS installer instead
  of overwriting a running binary. Ferrite implements check, staging, verification, and
  hand-off; publishing a feed requires externally hosted infrastructure.
