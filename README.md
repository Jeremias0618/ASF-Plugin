<p align="center">
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/csharp/csharp-original.svg" alt="ASF-Plugin" width="96" height="96" />
</p>

<h1 align="center">ASF-Plugin</h1>

<p align="center">
  Plugins personalizados para ArchiSteamFarm (comandos / IPC sociales).<br/>
  No es un fork del core ASF.
</p>

<p align="center">
  <a href="https://github.com/Jeremias0618/ASF-Plugin">
    <img src="https://img.shields.io/badge/repo-Jeremias0618%2FASF--Plugin-181717?style=flat&logo=github&logoColor=white" alt="Repositorio" />
  </a>
  <a href="https://github.com/JustArchiNET/ArchiSteamFarm">
    <img src="https://img.shields.io/badge/core-ArchiSteamFarm-512BD4?style=flat&logo=dotnet&logoColor=white" alt="ASF" />
  </a>
  <img src="https://img.shields.io/badge/C%23-plugin-239120?style=flat&logo=csharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/template-ASF--PluginTemplate-blue?style=flat" alt="Template" />
</p>

<p align="center">
  <a href="https://hits.sh/github.com/Jeremias0618/ASF-Plugin/">
    <img src="https://hits.sh/github.com/Jeremias0618/ASF-Plugin.svg?style=for-the-badge&label=Visitors&color=0e75b6" alt="Visitas al repositorio" />
  </a>
</p>

---

## Repositorio

| Remoto | URL |
|--------|-----|
| **Este proyecto (`origin`)** | https://github.com/Jeremias0618/ASF-Plugin |

> [!IMPORTANT]
> **No** es un fork de [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm). Es un repo de plugin(s) que se cargan en el exe **oficial**. JustArchiNET no garantiza el mismo nivel “vanilla” si usas plugins de terceros.

## Descripción

Plugins C# que se cargan en el **exe oficial** de ArchiSteamFarm (`ASF-BOT/plugins/…`).  
La UI asociada: [Jeremias0618/ASF-ui](https://github.com/Jeremias0618/ASF-ui).

### Plugins en este monorepo

| Carpeta | Qué hace | Estado |
|---------|----------|--------|
| [`IpcConfig/`](IpcConfig/) | API `GET/PUT/DELETE /Api/IpcConfig` para leer/escribir `config/IPC.config` desde el panel `/configuration` | Compilable (`build.ps1` → `ASF-BOT/plugins/IpcConfig/`); UI cableada; falta primer release ZIP en GitHub |

Plantillas / ejemplos externos:

- https://github.com/JustArchiNET/ASF-PluginTemplate  
- https://github.com/WiLuX-Source/ASF-FriendManager  

## Relacionado

| Pieza | Repo |
|-------|------|
| Este repo | https://github.com/Jeremias0618/ASF-Plugin |
| UI (fork) | https://github.com/Jeremias0618/ASF-ui |
| Core oficial | https://github.com/JustArchiNET/ArchiSteamFarm |
| Wiki plugins | https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Plugins |

## Estado

1. **IpcConfig** — contrato y código de referencia listos; siguiente: `.csproj` cableado a ASF (Template) + ZIP en Releases  
2. Otros plugins sociales (amigos/grupos/…) — pendientes  

## Instalación (cuando existan Releases)

1. Ir a https://github.com/Jeremias0618/ASF-Plugin/releases  
2. Descargar el ZIP del plugin (p. ej. **IpcConfig**) compatible con tu ASF  
3. Extraer en `ASF-BOT/plugins/IpcConfig/` (ver [IpcConfig/README.md](IpcConfig/README.md))  
4. Reiniciar ArchiSteamFarm y comprobar el log / pestaña Plugins  

> [!TIP]
> Mientras el plugin propio no cubra todo, puedes usar [ASFEnhance](https://github.com/chr233/ASFEnhance) u otros de la [lista third-party](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Third-party).

## Desarrollo (previsto)

```bash
git clone https://github.com/Jeremias0618/ASF-Plugin.git
cd ASF-Plugin
# Tras inicializar con PluginTemplate:
# git submodule update --init --recursive
# build del proyecto C# → empaquetar DLL en ZIP
```

Detalle técnico de IpcConfig: [IpcConfig/TECHNICAL.md](IpcConfig/TECHNICAL.md)

> [!WARNING]
> Abusar de solicitudes de amistad, follows o likes puede chocar con las normas de Steam. Usa rate-limits y sentido común.

## Créditos y licencia

- Ecosistema ASF: JustArchi / JustArchiNET (Apache-2.0 en core y ASF-ui)  
- Este repo: https://github.com/Jeremias0618/ASF-Plugin — licencia a definir al primer release de código (respetando bases Apache-2.0 / plantilla)
