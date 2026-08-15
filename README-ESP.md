<p align="center">
  <img src="https://cdn.simpleicons.org/steam/1B2838" alt="ASF-Plugin" width="96" height="96" />
</p>

<h1 align="center">ASF-Plugin</h1>

<p align="center">
  Plugins personalizados de <strong>ArchiSteamFarm</strong> (IPC para UI social / inventario).<br/>
  No es un fork del núcleo de ASF.
</p>

<p align="center">
  <a href="README.md">English</a> · <strong>Español</strong>
</p>

<p align="center">
  <a href="https://github.com/Jeremias0618/ASF-Plugin/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Jeremias0618/ASF-Plugin/ci.yml?branch=main&label=CI" alt="CI" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License" /></a>
  <img src="https://img.shields.io/badge/ASF-6.3.8.4-informational" alt="ASF target" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET" />
  <a href="https://hits.sh/github.com/Jeremias0618/ASF-Plugin/">
    <img src="https://hits.sh/github.com/Jeremias0618/ASF-Plugin.svg?style=for-the-badge&label=Visitors&color=0e75b6" alt="Visitas al repositorio" />
  </a>
</p>

---

## Repositorio

| Remoto | URL |
|--------|-----|
| **origin** | https://github.com/Jeremias0618/ASF-Plugin |

> [!IMPORTANT]
> Esto **no** es un fork de [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm). Los plugins se cargan en el binario **oficial** de ASF.

## Plugin activo: `ASFBotSocial`

| Ruta | Rol | Versión |
|------|-----|---------|
| `ASFBotSocial/` | IPC JSON: amigos, comunidad, juegos, wishlist, transferencia de inventario, ofertas de intercambio | **1.1.50** |

Lo consume [ASF-ui](https://github.com/Jeremias0618/ASF-ui) en los modales sociales del bot (`/bot/:name/…`).

### Estructura

```text
ASF-Plugin/
├── .github/workflows/     # CI + Release
├── ASFBotSocial/          # proyecto del plugin
│   ├── Controllers/
│   ├── Models/
│   └── Services/          # Common, Friends, Games, Inventory, …
├── ASFBotSocial.sln
├── Directory.Build.props
├── CHANGELOG.md
├── README.md              # guía en inglés
└── README-ESP.md          # esta guía
```

### IPC (resumen)

Prefijo: `/Api/BotSocial/{botNames}/…`  
Auth: la misma que el IPC de ASF (`Authentication` / `IPCPassword`).

| Área | Ejemplos |
|------|----------|
| Estado | `GET /Status` |
| Amigos | `GET /Friends`, `POST /Friends/Add\|Remove` |
| Comunidad | Groups, Followers, Curators, Reviews, SharedFiles |
| Juegos | Library, Search, Stats, Achievements, Add, DiscoveryQueue |
| Wishlist | List / Add / Remove / FollowAndAdd |
| Inventario | `POST /Inventory/Transfer`, TradeOffers |

La **lectura** de inventario usa el IPC oficial de ASF (`GET /Api/Bot/{bot}/Inventory…`). La transferencia está en este plugin.

### Compatibilidad

- El DLL del plugin debe coincidir con la versión **exacta** de `ArchiSteamFarm.exe` (strong-name).
- Objetivo actual: **6.3.8.4** (`ASFTargetVersion` en el `.csproj`).
- Reinicia ASF después de copiar el DLL.
- Las mutaciones tienen rate limit. El abuso puede vulnerar los ToS de Steam.

## Desarrollo

La referencia al proyecto ASF se resuelve desde `../ArchiSteamFarm` (monorepo) o `./ArchiSteamFarm` (clona el [repo oficial](https://github.com/JustArchiNET/ArchiSteamFarm) en el tag correspondiente).

```powershell
dotnet restore ASFBotSocial/ASFBotSocial.csproj
dotnet build ASFBotSocial/ASFBotSocial.csproj -c Release
```

## CI / CD

| Workflow | Disparador | Propósito |
|----------|------------|-----------|
| **Plugin CI** | push/PR a `main`/`develop` | Build Release (Ubuntu), artefacto DLL |
| **Plugin Release** | tag `v*` | ZIP + GitHub Release |

Ver [CONTRIBUTING.md](CONTRIBUTING.md) (en inglés).

## Relacionado

| Pieza | Repo |
|-------|------|
| UI | https://github.com/Jeremias0618/ASF-ui |
| Núcleo | https://github.com/JustArchiNET/ArchiSteamFarm |
| Plantilla | https://github.com/JustArchiNET/ASF-PluginTemplate |

> [!WARNING]
> Solicitudes masivas de amistad o spam de wishlist pueden violar las reglas de Steam. Prefiere acciones por lote con ritmo desde ASF-ui.
