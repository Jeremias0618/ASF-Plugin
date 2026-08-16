<p align="center">
  <img src=".github/previews/steam-logo-transparent.png" alt="ASF-Plugin" width="96" height="96" />
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
  <a href="https://github.com/Jeremias0618/ASF-Plugin/actions/workflows/ci.yml"><img src="https://github.com/Jeremias0618/ASF-Plugin/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License" /></a>
  <img src="https://img.shields.io/badge/ASF-6.3.8.4-informational" alt="ASF target" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET" />
  <a href="https://github.com/Jeremias0618/ASF-Plugin/releases">
    <img src="https://img.shields.io/github/downloads/Jeremias0618/ASF-Plugin/total?style=flat&label=descargas" alt="Descargas de GitHub Releases" />
  </a>
</p>

<p align="center">
  <a href="https://hits.sh/github.com/Jeremias0618/ASF-Plugin/">
    <img src="https://hits.sh/github.com/Jeremias0618/ASF-Plugin.svg?style=flat-square&label=visitors&color=0e75b6" alt="Visitas al repositorio" />
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

> [!IMPORTANT]
> **UI obligatoria:** este plugin solo se puede usar con la interfaz web modificada [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui). La UI oficial [JustArchiNET/ASF-ui](https://github.com/JustArchiNET/ASF-ui) (el `www/` que trae ASF) no incluye las rutas, páginas ni el HTML de Bot Social. El DLL expone IPC; sin ese fork no verás las pantallas de amigos, comunidad, juegos, wishlist ni transferencia de inventario.

## Instalación (release compilado)

Los usuarios **no** necesitan .NET. Descarga **ASFBotSocial.zip** desde [GitHub Releases](https://github.com/Jeremias0618/ASF-Plugin/releases).

1. Detén ArchiSteamFarm.
2. Descarga **ASFBotSocial.zip** del último release.
3. Extrae el ZIP en **`plugins/ASFBotSocial/`** (junto a `ArchiSteamFarm.exe`, dentro de `plugins/`), reemplazando el DLL si ya existía.
4. Arranca ASF.

### CLI

Detén ASF primero. Sustituye `RUTA_DE_TU_ARCHISTEAMFARM` por la carpeta que contiene `ArchiSteamFarm.exe` y `plugins/`, y desde ahí descarga y extrae.

> [!NOTE]
> En PowerShell de Windows **no** uses `curl` ni `unzip` (son de Linux; `curl` ahí es un alias de `Invoke-WebRequest`). Copia el bloque **Windows**.

**Windows (PowerShell)**

```powershell
cd "RUTA_DE_TU_ARCHISTEAMFARM"

curl.exe -L -o ASFBotSocial.zip "https://github.com/Jeremias0618/ASF-Plugin/releases/latest/download/ASFBotSocial.zip"
New-Item -ItemType Directory -Force -Path "plugins\ASFBotSocial" | Out-Null
Expand-Archive -Path "ASFBotSocial.zip" -DestinationPath "plugins\ASFBotSocial" -Force
Remove-Item "ASFBotSocial.zip"
```

**Linux / macOS**

```bash
cd "RUTA_DE_TU_ARCHISTEAMFARM"

curl -L -o ASFBotSocial.zip "https://github.com/Jeremias0618/ASF-Plugin/releases/latest/download/ASFBotSocial.zip"
mkdir -p plugins/ASFBotSocial
unzip -o ASFBotSocial.zip -d plugins/ASFBotSocial
rm ASFBotSocial.zip
```

Queda `plugins/ASFBotSocial/ASFBotSocial.dll`. Inicia ASF.

## Estructura

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

## IPC (resumen)

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

## Compatibilidad

- **Web UI:** solo [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui). La UI oficial de ASF **no** es compatible.
- El DLL del plugin debe coincidir con la versión **exacta** de `ArchiSteamFarm.exe` (strong-name).
- Objetivo actual: **6.3.8.4** (`ASFTargetVersion` en el `.csproj`).
- Reinicia ASF después de copiar el DLL (y de desplegar la UI del fork en `www/`).
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
| UI (obligatoria) | https://github.com/Jeremias0618/ASF-ui |
| Núcleo | https://github.com/JustArchiNET/ArchiSteamFarm |
| Plantilla | https://github.com/JustArchiNET/ASF-PluginTemplate |

> [!WARNING]
> Solicitudes masivas de amistad o spam de wishlist pueden violar las reglas de Steam. Prefiere acciones por lote con ritmo desde ASF-ui.
