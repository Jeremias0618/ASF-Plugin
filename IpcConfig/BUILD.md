# Compilar e instalar IpcConfig

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Fuentes de ASF en `ArchiSteamFarm/` (monorepo)
- La **versión del plugin debe coincidir** con `ASF-BOT\ArchiSteamFarm.exe` (strong-name). Si el exe es `6.3.8.4` y compilas contra `6.3.9.0`, ASF falla al cargar plugins.

## Build rápido → ASF-BOT

```powershell
cd ASF-Plugin\IpcConfig\src
.\build.ps1
```

El script lee la versión del exe en `ASF-BOT` y publica `IpcConfig.dll` en:

```text
ASF-BOT/plugins/IpcConfig/
```

Reinicia `ArchiSteamFarm.exe`. Log esperado:

```text
IpcConfig 1.0.0.0 loaded. Endpoints: GET|PUT|DELETE /Api/IpcConfig
```

Forzar versión a mano:

```powershell
.\build.ps1 -ASFTargetVersion 6.3.8.4
```

## Comandos manuales

```powershell
dotnet publish ASF-Plugin\IpcConfig\src\IpcConfig.csproj -c Release -o out -p:ASFTargetVersion=6.3.8.4
# Copiar out\IpcConfig.dll a ASF-BOT\plugins\IpcConfig\
```

## Empaquetar ZIP para Releases (GitHub)

Publica un ZIP por cada versión de ASF que soportes (o documenta la mínima). El asset debe llamarse **`IpcConfig.zip`**.

```powershell
dotnet publish ASF-Plugin\IpcConfig\src\IpcConfig.csproj -c Release -o staging\IpcConfig -p:ASFTargetVersion=6.3.8.4
Compress-Archive -Path staging\IpcConfig\* -DestinationPath IpcConfig.zip -Force
```

## Front (ASF-ui)

`/configuration` detecta el plugin, aplica con `PUT /Api/IpcConfig`, o avisa si falta.
