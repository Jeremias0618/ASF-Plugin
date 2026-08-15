<p align="center">
  <img src="https://cdn.simpleicons.org/steam/1B2838" alt="ASF-Plugin" width="96" height="96" />
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
  <a href="https://github.com/Jeremias0618/ASF-Plugin/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Jeremias0618/ASF-Plugin/ci.yml?branch=main&label=CI" alt="CI" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License" /></a>
  <img src="https://img.shields.io/badge/ASF-6.3.8.4-informational" alt="ASF target" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET" />
  <a href="https://hits.sh/github.com/Jeremias0618/ASF-Plugin/">
    <img src="https://hits.sh/github.com/Jeremias0618/ASF-Plugin.svg?style=for-the-badge&label=Visitors&color=0e75b6" alt="Repository visitors" />
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

### Layout

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

### IPC (summary)

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

### Compatibility

- Plugin DLL must match the **exact** `ArchiSteamFarm.exe` version (strong-name).
- Current target: **6.3.8.4** (`ASFTargetVersion` in the `.csproj`).
- Restart ASF after copying the DLL.
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
| UI | https://github.com/Jeremias0618/ASF-ui |
| Core | https://github.com/JustArchiNET/ArchiSteamFarm |
| Template | https://github.com/JustArchiNET/ASF-PluginTemplate |

> [!WARNING]
> Mass friend requests or wishlist spam can violate Steam rules. Prefer paced multi-actions from ASF-ui.
