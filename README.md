<p align="center">
  <img src=".github/previews/steam-logo-transparent.png" alt="ASF-Plugin" width="96" height="96" />
</p>

<h1 align="center">ASF-Plugin</h1>

<p align="center">
  Custom <strong>ArchiSteamFarm</strong> plugins (IPC for social / inventory UI).<br/>
  Not a fork of the ASF core.
</p>

<p align="center">
  <strong>English</strong> · <a href="README-ESP.md">Español</a>
</p>

<p align="center">
  <a href="https://github.com/Jeremias0618/ASF-Plugin/actions/workflows/ci.yml"><img src="https://github.com/Jeremias0618/ASF-Plugin/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License" /></a>
  <img src="https://img.shields.io/badge/ASF-6.3.10.3-informational" alt="ASF target" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET" />
  <a href="https://github.com/Jeremias0618/ASF-Plugin/releases">
    <img src="https://img.shields.io/github/downloads/Jeremias0618/ASF-Plugin/total?style=flat&label=downloads" alt="GitHub Releases downloads" />
  </a>
</p>

<p align="center">
  <a href="https://hits.sh/github.com/Jeremias0618/ASF-Plugin/">
    <img src="https://hits.sh/github.com/Jeremias0618/ASF-Plugin.svg?style=flat-square&label=visitors&color=0e75b6" alt="Repository visitors" />
  </a>
</p>

---

## Repository

| Remote | URL |
|--------|-----|
| **origin** | https://github.com/Jeremias0618/ASF-Plugin |

> [!IMPORTANT]
> This is **not** a fork of [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm). Plugins load into the **official** ASF binary.

## Active plugin: `ASFBotSocial`

| Path | Role | Version |
|------|------|---------|
| `ASFBotSocial/` | IPC JSON: friends, community, games, wishlist, inventory transfer, trade offers | **1.1.51** | 

Consumed by [ASF-ui](https://github.com/Jeremias0618/ASF-ui) bot social modals (`/bot/:name/…`).

> [!IMPORTANT]
> **UI required:** this plugin only works with the modified web UI at [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui). The official [JustArchiNET/ASF-ui](https://github.com/JustArchiNET/ASF-ui) (the `www/` that ships with ASF) has no routes, pages, or HTML for Bot Social. The DLL exposes IPC; without that fork you will not see friends, community, games, wishlist, or inventory-transfer screens.

## Install (compiled release)

End users do **not** need .NET. Download **ASFBotSocial.zip** from [GitHub Releases](https://github.com/Jeremias0618/ASF-Plugin/releases).

1. Stop ArchiSteamFarm.
2. Download **ASFBotSocial.zip** from the latest release.
3. Extract it into **`plugins/ASFBotSocial/`** (next to `ArchiSteamFarm.exe`, inside `plugins/`), replacing the DLL if it already exists.
4. Start ASF.

### CLI

Stop ASF first. Replace `PATH_TO_YOUR_ARCHISTEAMFARM` with the folder that contains `ArchiSteamFarm.exe` and `plugins/`, then download and extract.

> [!NOTE]
> On Windows PowerShell do **not** use `curl` or `unzip` (those are Linux commands; `curl` there is an alias for `Invoke-WebRequest`). Copy the **Windows** block.

**Windows (PowerShell)**

```powershell
cd "PATH_TO_YOUR_ARCHISTEAMFARM"

curl.exe -L -o ASFBotSocial.zip "https://github.com/Jeremias0618/ASF-Plugin/releases/latest/download/ASFBotSocial.zip"
New-Item -ItemType Directory -Force -Path "plugins\ASFBotSocial" | Out-Null
Expand-Archive -Path "ASFBotSocial.zip" -DestinationPath "plugins\ASFBotSocial" -Force
Remove-Item "ASFBotSocial.zip"
```

**Linux / macOS**

```bash
cd "PATH_TO_YOUR_ARCHISTEAMFARM"

curl -L -o ASFBotSocial.zip "https://github.com/Jeremias0618/ASF-Plugin/releases/latest/download/ASFBotSocial.zip"
mkdir -p plugins/ASFBotSocial
unzip -o ASFBotSocial.zip -d plugins/ASFBotSocial
rm ASFBotSocial.zip
```

This leaves `plugins/ASFBotSocial/ASFBotSocial.dll`. Start ASF.

## Layout

```text
ASF-Plugin/
├── .github/workflows/     # CI + Release
├── ASFBotSocial/          # plugin project
│   ├── Controllers/
│   ├── Models/
│   └── Services/          # Common, Friends, Games, Inventory, …
├── ASFBotSocial.sln
├── Directory.Build.props
├── CHANGELOG.md
├── README.md              # this guide
└── README-ESP.md          # Spanish guide
```

## IPC (summary)

Prefix: `/Api/BotSocial/{botNames}/…`  
Auth: same as ASF IPC (`Authentication` / `IPCPassword`).

| Area | Examples |
|------|----------|
| Status | `GET /Status` |
| Friends | `GET /Friends`, `POST /Friends/Add\|Remove` |
| Community | Groups, Followers, Curators, Reviews, SharedFiles |
| Games | Library, Search, Stats, Achievements, Add, DiscoveryQueue |
| Wishlist | List / Add / Remove / FollowAndAdd |
| Inventory | `POST /Inventory/Transfer`, TradeOffers |
| Own reviews | `GET /OwnReviews?page=N`, `GET /OwnReviews/{appId}`, `GET /OwnReviews/{appId}/Editor`, `POST /OwnReviews`, `PUT /OwnReviews/{appId}` (one bot per request) |

Inventory **read** uses official ASF IPC (`GET /Api/Bot/{bot}/Inventory…`). Transfer lives in this plugin.

### Own reviews (`/OwnReviews`)

Reviews written by the **bot account itself**. The author is always the bot's own SteamID64 (there is no author parameter), IDs travel as strings and each request accepts exactly one bot. `Reviews/Vote` is unchanged.

- `GET /OwnReviews?page=N` — 10 entries per page (fixed by Steam). `Total`, `HasMore` and `Complete` say whether the list was read to its end; `Status: empty_unconfirmed` means Steam returned nothing and gave no total (no reviews **or** an unreadable profile).
- `GET /OwnReviews/{appId}` — full text, recommendation, playtime text, helpful/funny counters and awards.
- `GET /OwnReviews/{appId}/Editor` — raw text exactly as stored, current recommendation and the 8000-character limit; used to fill the edit form. Only answers when Steam renders the owner form (i.e. the review belongs to the bot).
- `PUT /OwnReviews/{appId}` — edit own review (`{ Recommended?, Text, ConfirmRatingReset }`). Ownership is checked server-side (the owner form must be present for the bot's own session). Changing the recommendation makes Steam clear the helpful/funny ratings, so it needs `ConfirmRatingReset: true`; without it the call fails with `rating_reset_unconfirmed` and nothing is sent. If nothing changed Steam is not called. Success is only reported (`Verified: true`) after the review is read back from Steam.
- `POST /OwnReviews` — publish (`{ AppId, Recommended, Text, Language }`). If the bot already has a review for that product the call answers `409 review_exists` so the client can offer to edit it. Same read-back confirmation.
- Writes are serialised per bot (`operation_in_progress` otherwise) and never log the review text.
- Every counter is `null` when Steam did not return it; `0` is only reported when Steam said so. The *not helpful* count is not exposed by Steam and is never derived.
- Errors carry a stable prefix in `Message` (`code: message`): `400 review_invalid`, `404 review_not_found`, `409 review_exists` / `rating_reset_unconfirmed` / `operation_in_progress`, `422 steam_rejected`, `503 session_expired` / `steam_unavailable` / `steam_unexpected_page` / `bot_not_connected`. `GET /Status` announces `reviews.own.read`, `reviews.own.publish` and `reviews.own.edit` separately so clients can detect an older DLL.
- Same cooldown conventions as the rest of the plugin (`EndpointRateLimiter` 1.5 s per bot for reads, 3 s for writes, and a 1.5 s Steam-side limiter).

**What is verified.** Steam has no JSON API for an author's review list, so this reads the community web pages (`/profiles/{id}/recommended/…?l=english`), which depends on Steam's markup. Checked anonymously against public profiles on 2026-11-25: paged listing (`Showing x-y of N entries`), helpful/funny counters, awards, recommendation, playtime, date, full text in the detail page. `store.steampowered.com/appreviews/{appid}?json=1` returned HTML in that probe, so it is **not** used. Not exposed on those pages: *not helpful* votes and comments (loaded dynamically).

**Verified with a real account (2026-11-26, own session, ASF 6.3.10.3).** Authenticated list (6 pages / 51 reviews), editor read, editing the text of an existing review from ASF-ui and through the API, read-back confirmation, restoring the original text, Unicode / BBCode / newline round trip, the no-change shortcut and the guard errors (`review_invalid`, `review_not_found`, `review_exists`). Editing uses `POST steamcommunity.com/userreviews/update/{reviewId}`; publishing uses `POST store.steampowered.com/friends/recommendgame` (the same calls the Steam web pages make).

**Not verified.** **Publishing a new review** against Steam (the mechanism is taken from Steam's own page script and form defaults; only the paths that fail before reaching Steam were exercised), **changing the recommendation** of an existing review (irreversible for ratings, so it was not tried) and **deleting** (not implemented: clients must send the user to Steam). Steam may reject a publish for reasons only visible at that moment (playtime, restricted accounts, rate limits); those come back as `steam_rejected`.

## Compatibility

- **Web UI:** [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui) only. Official ASF-ui is **not** compatible.
- Plugin DLL must match the **exact** `ArchiSteamFarm.exe` version (strong-name).
- Current target: **6.3.10.3** (`ASFTargetVersion` in `Directory.Build.props` — single source of truth).
- Restart ASF after copying the DLL (and after deploying the fork UI to `www/`).
- Mutations are rate-limited. Abuse risks Steam ToS issues.
- **Steam sign-in is not part of this plugin.** ASF-ui's login wizard uses only the official ASF IPC and works without `ASFBotSocial`. ASF's own `Y/N` console questions (QR vs credentials, mobile approval vs code) cannot be answered through plugin interfaces; see the table in the [ASF-ui README](https://github.com/Jeremias0618/ASF-ui#steam-sign-in-wizard-botnamelogin).

## Development

ASF project reference resolves from `../ArchiSteamFarm` (monorepo) or `./ArchiSteamFarm` (clone the [official repo](https://github.com/JustArchiNET/ArchiSteamFarm) at the matching tag).

```powershell
dotnet restore ASFBotSocial/ASFBotSocial.csproj
dotnet build ASFBotSocial/ASFBotSocial.csproj -c Release
```

## CI / CD

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| **Plugin CI** | push/PR to `main`/`develop` | Release build (Ubuntu), artifact DLL |
| **Update ASF target** | daily cron + manual | Detect new [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) releases → open PR (allowlisted files only) |
| **Release on version bump** | push to `main` (version change) | Build + bind verify → tag `vX.Y.Z` → ZIP (no tag before green build) |
| **Plugin Release** | tag `v*` | Same verify path; skips if release already exists |

ASF version is **not** duplicated in workflows: CI/Release read `Directory.Build.props`. CI checks ASF git tag + DLL assembly bind; runtime smoke test remains manual. Merge ASF bump PRs only after CI is green. Enable branch protection as in [CONTRIBUTING.md](CONTRIBUTING.md).

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Related

| Piece | Repo |
|-------|------|
| UI (required) | https://github.com/Jeremias0618/ASF-ui |
| Core | https://github.com/JustArchiNET/ArchiSteamFarm |
| Template | https://github.com/JustArchiNET/ASF-PluginTemplate |

> [!WARNING]
> Mass friend requests or wishlist spam can violate Steam rules. Prefer paced multi-actions from ASF-ui.
