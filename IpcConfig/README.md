<p align="center">
  <img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/csharp/csharp-original.svg" alt="IpcConfig" width="72" height="72" />
</p>

<h1 align="center">IpcConfig</h1>

<p align="center">
  Plugin de ArchiSteamFarm que expone API IPC para <strong>leer y escribir</strong>
  <code>config/IPC.config</code> desde ASF-ui (página <code>/configuration</code>).
</p>

---

## Para qué sirve

ASF oficial **no** tiene endpoint para guardar la configuración de red de Kestrel (`IPC.config`).  
Este plugin añade API autenticada para que el front en `ASF-BOT/www` pueda **aplicar** el archivo en `ASF-BOT/config/IPC.config`.

## Alcance

| Incluye | No incluye |
|---------|------------|
| GET/PUT/DELETE de `IPC.config` | Cambiar `IPCPassword` (API oficial `/Api/Asf`) |
| Validación de URL/puerto/CIDR | ACL por IP de clientes (ver TECHNICAL) |
| Recarga vía ConfigWatch del core | Sustituir ASF-config / UI-config |
| Compatible con ASF-ui `/configuration` | Instalar el plugin “a ciegas” sin ZIP (ASF no lo permite) |

## Instalación en ASF-BOT

1. Descarga **`IpcConfig.zip`** del [release](https://github.com/Jeremias0618/ASF-Plugin/releases) **o** compílalo aquí ([BUILD.md](BUILD.md)).
2. Extrae **solo la DLL** en:

```text
ASF-BOT/plugins/IpcConfig/IpcConfig.dll
```

3. Reinicia `ArchiSteamFarm.exe` → debe aparecer **IpcConfig** en Plugins.
4. Panel → **Configuration** → **Apply network settings**.

> [!IMPORTANT]
> No copies la carpeta fuente (`*.cs`). Solo el resultado de `dotnet publish` / `build.ps1`.

> [!WARNING]
> Escuchar en `http://*:1242` abre el panel a la LAN. Usa siempre `IPCPassword`.

## Compilar (dev)

```powershell
cd ASF-Plugin\IpcConfig\src
.\build.ps1
```

Detalle: [BUILD.md](BUILD.md) · Técnico: [TECHNICAL.md](TECHNICAL.md)

## Relacionado

| Pieza | Ubicación |
|-------|-----------|
| Este plugin | `ASF-Plugin/IpcConfig/` |
| UI | `ASF-ui` → `/configuration` |
| Runtime | `ASF-BOT/` |
| Wiki IPC | https://github.com/JustArchiNET/ArchiSteamFarm/wiki/IPC |
