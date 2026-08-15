# Contributing

## Prerequisites

- .NET SDK **10**
- ArchiSteamFarm sources matching `ASFTargetVersion` in `ASFBotSocial/ASFBotSocial.csproj`

### ASF reference

| Layout | Path |
|--------|------|
| Monorepo sibling | `../ArchiSteamFarm` (preferred when developing next to ASF-ui / ASF-BOT) |
| Standalone | Clone [JustArchiNET/ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) into `./ArchiSteamFarm` at the matching tag |

## Local validation

```powershell
dotnet restore ASFBotSocial/ASFBotSocial.csproj
dotnet build ASFBotSocial/ASFBotSocial.csproj -c Debug
dotnet build ASFBotSocial/ASFBotSocial.csproj -c Release
```

## Pull requests

1. Branch from `main` (`feature/*` or `bugfix/*`).
2. Keep PRs focused; update `CHANGELOG.md` for user-visible IPC / behavior changes.
3. CI (`Plugin CI`) must pass on Debug + Release.

## Releases

1. Bump `<Version>` in `ASFBotSocial.csproj`.
2. Tag `vX.Y.Z` and push — `Plugin Release` packs `ASFBotSocial.zip` to GitHub Releases.
