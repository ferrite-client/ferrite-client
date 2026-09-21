# Human Action Required

Items where a person must supply something this environment cannot produce. Each entry states
what is needed, why, the exact steps, what to hand back, and what verification stays blocked
until then. Nothing in this file blocks unrelated development.

---

## H1 - Microsoft / Azure application client ID (auth)

**Needed.** An Azure application (client) ID for a *public client* app registration, or
permission to use an existing one.

**Why.** Microsoft sign-in requires a client ID that Microsoft recognises. Ferrite deliberately
ships without a bundled ID so no shared credential is embedded in the product, and because
using someone else's registration would be improper.

**Steps.**

1. Open the Azure portal -> Microsoft Entra ID -> App registrations -> New registration.
2. Name it (for example `Ferrite Launcher`).
3. Supported account types: *Accounts in any organizational directory and personal Microsoft
   accounts*.
4. Redirect URI: leave empty for the device-code flow; if you also want the browser flow, add
   a public client/native redirect of `http://localhost` .
5. Under *Authentication*, enable *Allow public client flows*.
6. Under *API permissions*, no Microsoft Graph permission is required for Minecraft sign-in.
7. Copy the *Application (client) ID*.

**What to hand back.** The client ID, entered in Ferrite under Settings -> Accounts ->
Microsoft client ID (it is stored in launcher settings, never in the repository).

**Blocked until then.** Live Microsoft sign-in and every launch that needs a real Minecraft
profile. Recorded as FEATURE_PARITY row F14.

---

## H2 - CurseForge API key

**Needed.** A CurseForge API key from the CurseForge developer console.

**Why.** CurseForge requires an issued key and applies redistribution restrictions. Ferrite
implements the complete client, but cannot legitimately call the service without a key.

**Steps.**

1. Sign in at the CurseForge developer console.
2. Create an API key and accept the terms.
3. Copy the key.

**What to hand back.** The key, entered in Ferrite under Settings -> Content providers ->
CurseForge API key. It is stored in the OS-protected secret store (`config/accounts.bin`,
DPAPI current-user on Windows), never in the settings document and never in the repository.

**Already implemented.** The API client (search, project, file listing, bulk file resolution,
download-URL lookup), version compatibility filtering, dependency resolution, the
`manifest.json` modpack installer with overrides extraction, and secure key storage, all
covered by automated tests against a scripted HTTP boundary. The verification harness reports
the blocked state with `Ferrite.Verify curseforge <query>`.

**Blocked until then.** Live CurseForge search, file resolution, and CurseForge modpack
downloads. Recorded as FEATURE_PARITY row K05. After the key is stored, verify with
`scripts/verify-live.ps1 curseforge jei` (expect real search hits) and
`scripts/verify-live.ps1 modpack <path-to-a-curseforge-pack.zip>` (expect files downloaded into
the new instance's `mods/` folder).

---

## H3 - Update feed hosting

**Needed.** An HTTPS location to host the feed and the release packages, plus a signing key pair whose
public half is built into the shipped launcher.

**Why.** Self-update requires a feed to read from and a key to trust. Ferrite implements the check,
signature verification, staging, SHA-256 verification, and hand-off, but it cannot host anything on
the user's behalf and must not ship with a key nobody controls.

**Steps.**

1. Package a release: `pwsh -File scripts/package.ps1 -Version 0.2.0`.
2. Build the feed: `dotnet run --project tools/Ferrite.Verify -- sign-update --version 0.2.0
   --package artifacts/ferrite-self-contained-win-x64.zip --out <feed> --kind self-contained`.
   This writes `manifest.json`, `manifest.json.sig`, and (on first use) a fresh ECDSA P-256 key
   pair. Reuse an existing key with `--private-key <pem>` on later releases.
3. Keep `update-private-key.pem` offline. Never commit it.
4. Copy `update-public-key.pem` to `src/Ferrite.App/Resources/update-public-key.pem` and rebuild, so
   that the shipped launcher trusts only this feed.
5. Upload the whole feed directory — `manifest.json`, `manifest.json.sig`, and the package zips —
   to the HTTPS location.
6. Enter that URL under Settings -> Launcher updates -> Update feed URL.

**What to hand back.** Nothing is entered by the user at runtime beyond the feed URL. The key pair
belongs to whoever publishes the release.

**Already implemented.** The signed-feed check, detached signature verification (ECDSA P-256 or
RSA), version comparison, SHA-256 and size verification of the package, staging outside the install
root, and the generated hand-off script. The publisher tooling is part of the verification harness.
`scripts/verify-live.ps1 update-check --feed <dir> --key <feed>/update-public-key.pem --current
0.1.0 --stage` runs the whole flow against a local feed over loopback HTTP.

**Blocked until then.** Delivering an update from a public host, which is a hosting and key-ownership
step rather than a code change. Recorded as FEATURE_PARITY row O09.

---

## H4 - Internet LAN relay service

**Needed.** A relay/rendezvous service, or a decision to accept that this capability stays
out of scope.

**Why.** Playing together over the internet as if on a LAN requires a relay both peers can
reach. Ferrite does not operate such a service and will not reuse XMCL's.

**Blocked until then.** Remote LAN play. Local LAN assistance is implemented.
Recorded as FEATURE_PARITY row M10.

---

## H5 - Code-signing certificate for the application

**Needed.** An Authenticode (OV or EV) code-signing certificate, or a decision to ship unsigned.

**Why.** Unsigned Windows executables trigger SmartScreen warnings. Ferrite's packaging produces
unsigned builds; signing is a certificate operation, not a code change, and no certificate is
available in this environment.

**Steps.**

1. Obtain a code-signing certificate (an OV or EV certificate from a public CA, or an
   organisation-issued one).
2. Sign both published executables, for example:
   `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f cert.pfx Ferrite.exe`.
3. If the build is ever repackaged, sign the executable before archiving it.

**What to hand back.** Nothing is entered into Ferrite; signing happens at packaging time. The
certificate itself must never be committed.

**Blocked until then.** A download that does not warn on first run. Recorded as FEATURE_PARITY
row A09.
