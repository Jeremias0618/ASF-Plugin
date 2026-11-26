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
  <img src="https://img.shields.io/badge/ASF-6.3.10.3-informational" alt="ASF target" />
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
| `ASFBotSocial/` | IPC JSON: amigos, comunidad, juegos, wishlist, transferencia de inventario, ofertas de intercambio | **1.1.51** |

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
| Reseñas propias | `GET /OwnReviews?page=N`, `GET /OwnReviews/{appId}`, `GET /OwnReviews/{appId}/Editor`, `POST /OwnReviews`, `PUT /OwnReviews/{appId}` (un bot por petición) |

La **lectura** de inventario usa el IPC oficial de ASF (`GET /Api/Bot/{bot}/Inventory…`). La transferencia está en este plugin.

### Reseñas propias (`/OwnReviews`)

Reseñas escritas por **la propia cuenta del bot**. El autor es siempre el SteamID64 del bot (no hay parámetro de autor), los IDs viajan como cadenas y cada petición acepta exactamente un bot. `Reviews/Vote` no cambia.

- `GET /OwnReviews?page=N` — 10 entradas por página (fijo en Steam). `Total`, `HasMore` y `Complete` indican si la lista se leyó hasta el final; `Status: empty_unconfirmed` significa que Steam no devolvió nada ni un total (no hay reseñas **o** el perfil no se puede leer).
- `GET /OwnReviews/{appId}` — texto completo, recomendación, tiempo de juego, contadores útil/divertida y premios.
- `GET /OwnReviews/{appId}/Editor` — texto en bruto tal como está guardado, recomendación actual y el límite de 8000 caracteres; sirve para rellenar el formulario de edición. Solo responde si Steam muestra el formulario de autor (es decir, la reseña es del bot).
- `PUT /OwnReviews/{appId}` — editar una reseña propia (`{ Recommended?, Text, ConfirmRatingReset }`). La propiedad se comprueba en el servidor (el formulario de autor debe aparecer con la sesión del propio bot). Cambiar la recomendación hace que Steam borre las valoraciones útil/divertida, así que exige `ConfirmRatingReset: true`; sin él falla con `rating_reset_unconfirmed` y no se envía nada. Si no hay cambios no se llama a Steam. Solo se informa éxito (`Verified: true`) tras releer la reseña desde Steam.
- `POST /OwnReviews` — publicar (`{ AppId, Recommended, Text, Language }`). Si el bot ya tiene una reseña de ese producto responde `409 review_exists` para que el cliente ofrezca editarla. Misma confirmación por relectura.
- Las escrituras se serializan por bot (`operation_in_progress` si no) y nunca registran el texto de la reseña en el log.
- Cada contador es `null` si Steam no lo devolvió; `0` solo se informa cuando Steam lo dijo. El contador de *no útil* no lo expone Steam y nunca se deduce.
- Los errores llevan un prefijo estable en `Message` (`código: mensaje`): `400 review_invalid`, `404 review_not_found`, `409 review_exists` / `rating_reset_unconfirmed` / `operation_in_progress`, `422 steam_rejected`, `503 session_expired` / `steam_unavailable` / `steam_unexpected_page` / `bot_not_connected`. `GET /Status` anuncia `reviews.own.read`, `reviews.own.publish` y `reviews.own.edit` por separado para que los clientes detecten una DLL antigua.
- Mismas convenciones de espera que el resto del plugin (`EndpointRateLimiter` de 1,5 s por bot en lecturas, 3 s en escrituras, y un limitador de 1,5 s hacia Steam).

**Qué está verificado.** Steam no tiene API JSON para la lista de reseñas de un autor, así que se leen las páginas web de la comunidad (`/profiles/{id}/recommended/…?l=english`), lo que depende del marcado de Steam. Comprobado de forma anónima con perfiles públicos el 2026-11-25: listado paginado (`Showing x-y of N entries`), contadores útil/divertida, premios, recomendación, tiempo de juego, fecha y texto completo en el detalle. `store.steampowered.com/appreviews/{appid}?json=1` devolvió HTML en esa prueba, así que **no** se usa. Esas páginas no exponen los votos *no útil* ni los comentarios (se cargan dinámicamente).

**Verificado con una cuenta real (2026-11-26, sesión propia, ASF 6.3.10.3).** Lista autenticada (6 páginas / 51 reseñas), lectura del editor, edición del texto de una reseña existente desde ASF-ui y por la API, confirmación por relectura, restauración del texto original, ida y vuelta de Unicode / BBCode / saltos de línea, el atajo sin cambios y los errores de guarda (`review_invalid`, `review_not_found`, `review_exists`). Editar usa `POST steamcommunity.com/userreviews/update/{reviewId}`; publicar usa `POST store.steampowered.com/friends/recommendgame` (las mismas llamadas que hacen las páginas web de Steam).

**No verificado.** **Publicar una reseña nueva** contra Steam (el mecanismo sale del script de la propia página de Steam y de los valores por defecto del formulario; solo se ejercitaron los caminos que fallan antes de llegar a Steam), **cambiar la recomendación** de una reseña existente (irreversible para las valoraciones, por eso no se probó) y **eliminar** (no implementado: los clientes deben enviar al usuario a Steam). Steam puede rechazar una publicación por motivos que solo se ven en ese momento (tiempo de juego, cuentas restringidas, límites); vuelven como `steam_rejected`.

## Compatibilidad

- **Web UI:** solo [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui). La UI oficial de ASF **no** es compatible.
- El DLL del plugin debe coincidir con la versión **exacta** de `ArchiSteamFarm.exe` (strong-name).
- Objetivo actual: **6.3.10.3** (`ASFTargetVersion` en `Directory.Build.props` — única fuente de verdad).
- Reinicia ASF después de copiar el DLL (y de desplegar la UI del fork en `www/`).
- Las mutaciones tienen rate limit. El abuso puede vulnerar los ToS de Steam.
- **El inicio de sesión en Steam no forma parte de este plugin.** El asistente de ASF-ui usa solo el IPC oficial de ASF y funciona sin `ASFBotSocial`. Las preguntas `Y/N` de la consola de ASF (QR o credenciales, aprobación móvil o código) no se pueden responder mediante interfaces de plugin; consulta la tabla del [README de ASF-ui](https://github.com/Jeremias0618/ASF-ui/blob/main/README-ESP.md#asistente-de-inicio-de-sesión-en-steam-botnombrelogin).

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
| **Update ASF target** | cron diario + manual | Detecta releases de [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) → abre PR (solo archivos en allowlist) |
| **Release on version bump** | push a `main` (cambio de versión) | Build + verificación de bind → tag `vX.Y.Z` → ZIP (nunca etiqueta antes de un build verde) |
| **Plugin Release** | tag `v*` | Misma verificación; omite si el release ya existe |

La versión de ASF **no** se duplica en los workflows: CI/Release leen `Directory.Build.props`. El CI comprueba el tag de ASF y el bind del DLL; el smoke test en runtime sigue siendo manual. Fusiona los PR de bump solo si el CI está en verde. Activa protección de rama según [CONTRIBUTING.md](CONTRIBUTING.md).

Ver [CONTRIBUTING.md](CONTRIBUTING.md) (en inglés).

## Relacionado

| Pieza | Repo |
|-------|------|
| UI (obligatoria) | https://github.com/Jeremias0618/ASF-ui |
| Núcleo | https://github.com/JustArchiNET/ArchiSteamFarm |
| Plantilla | https://github.com/JustArchiNET/ASF-PluginTemplate |

> [!WARNING]
> Solicitudes masivas de amistad o spam de wishlist pueden violar las reglas de Steam. Prefiere acciones por lote con ritmo desde ASF-ui.
