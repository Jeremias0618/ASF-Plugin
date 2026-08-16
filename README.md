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
  <img src="https://img.shields.io/badge/ASF-6.3.8.4-informational" alt="ASF target" />
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
| `ASFBotSocial/` | IPC JSON: friends, community, games, wishlist, inventory transfer, trade offers | **1.1.50** |

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

Inventory **read** uses official ASF IPC (`GET /Api/Bot/{bot}/Inventory…`). Transfer lives in this plugin.

## Compatibility

- **Web UI:** [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui) only. Official ASF-ui is **not** compatible.
- Plugin DLL must match the **exact** `ArchiSteamFarm.exe` version (strong-name).
- Current target: **6.3.8.4** (`ASFTargetVersion` in the `.csproj`).
- Restart ASF after copying the DLL (and after deploying the fork UI to `www/`).
- Mutations are rate-limited. Abuse risks Steam ToS issues.

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
| **Plugin Release** | tag `v*` | ZIP + GitHub Release |

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Related

| Piece | Repo |
|-------|------|
| UI (required) | https://github.com/Jeremias0618/ASF-ui |
| Core | https://github.com/JustArchiNET/ArchiSteamFarm |
| Template | https://github.com/JustArchiNET/ASF-PluginTemplate |

> [!WARNING]
> Mass friend requests or wishlist spam can violate Steam rules. Prefer paced multi-actions from ASF-ui.
