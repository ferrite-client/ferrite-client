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

**Needed.** A hosted update manifest and package location, and a signing certificate for
release artefacts.

**Why.** Self-update requires a feed to read from and a signature to trust. Ferrite implements
the check, staging, verification, and hand-off, but cannot publish a feed itself.

**Blocked until then.** Live update delivery. Recorded as FEATURE_PARITY row O09.

---

## H4 - Internet LAN relay service

**Needed.** A relay/rendezvous service, or a decision to accept that this capability stays
out of scope.

**Why.** Playing together over the internet as if on a LAN requires a relay both peers can
reach. Ferrite does not operate such a service and will not reuse XMCL's.

**Blocked until then.** Remote LAN play. Local LAN assistance is implemented.
Recorded as FEATURE_PARITY row M10.
