# Contributing

## Prerequisites

- .NET SDK **10**
- ArchiSteamFarm sources matching `ASFTargetVersion` in **`Directory.Build.props`** (single source of truth)

### ASF reference

| Layout | Path |
|--------|------|
| Monorepo sibling | `../ArchiSteamFarm` (preferred when developing next to ASF-ui / ASF-BOT) |
| Standalone | Clone [JustArchiNET/ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) into `./ArchiSteamFarm` at the matching tag |

```powershell
# Example: sync local ASF checkout to the version this plugin targets
$ver = (Select-String -Path Directory.Build.props -Pattern '<ASFTargetVersion>([^<]+)').Matches.Groups[1].Value
git -C .\ArchiSteamFarm fetch --tags
git -C .\ArchiSteamFarm checkout $ver
```

## Local validation

```powershell
dotnet restore ASFBotSocial/ASFBotSocial.csproj
dotnet build ASFBotSocial/ASFBotSocial.csproj -c Debug
dotnet build ASFBotSocial/ASFBotSocial.csproj -c Release
# Optional bind check (same script as CI):
# .\.github\scripts\Verify-AsfBind.ps1 -PluginDll ... -ExpectedAsfVersion $ver -AsfCheckoutPath .\ArchiSteamFarm
```

## Pull requests

1. Branch from `main` (`feature/*` or `bugfix/*`).
2. Keep PRs focused; update `CHANGELOG.md` for user-visible IPC / behavior changes.
3. CI (`Plugin CI`) must pass (Release build + ASF bind verify on Ubuntu).

## ASF version updates

Do **not** hardcode ASF versions in `ci.yml` / `release.yml`.

| Mechanism | Role |
|-----------|------|
| `Directory.Build.props` → `ASFTargetVersion` | Only place to set the ASF tag |
| Workflow **Update ASF target** | Daily check of [ArchiSteamFarm releases](https://github.com/JustArchiNET/ArchiSteamFarm/releases); opens a PR with label `asf-bump` |
| Workflow **Release on version bump** | On merge to `main`: **build + verify bind first**, then tag `vX.Y.Z` and publish ZIP |
| Workflow **Plugin Release** | Manual/tag path with the same build + bind verify |
| `.github/scripts/Verify-AsfBind.ps1` | Asserts ASF git tag + plugin DLL references `ArchiSteamFarm` at that version |

Default policy: **manual merge** of `asf-bump` PRs after CI is green. Optionally enable auto-merge later.

Manual force bump (Actions → Update ASF target → Run workflow):

- `force_version`: e.g. `6.3.10.3`
- `include_prereleases`: include ASF release candidates

`renovate.json` can update NuGet / GitHub Actions. The ASF custom manager is **disabled** there so it does not duplicate `update-asf.yml`.

### Compatibility limits

CI verifies **compile-time / strong-name bind** (correct ASF tag checked out + DLL references that assembly version). It does **not** start `ArchiSteamFarm.exe` or talk to Steam. After publishing, smoke-test locally: plugin loads, `GET /Api/BotSocial/{bot}/Status`.

## Branch protection (required for production)

In GitHub → Settings → Branches → rule for `main`:

1. **Require a pull request before merging**.
2. **Require status checks**: `Build (Release)` from Plugin CI.
3. **Require review from Code Owners** (`.github/CODEOWNERS`).
4. Restrict who can push to `main` / bypass protections if your plan allows.
5. Never switch CI to `pull_request_target`. Keep write workflows off untrusted PR code.

Write-capable workflows (`update-asf`, `release-on-bump`, `release`) run only on `schedule` / `workflow_dispatch` / `push` to `main` or tags. `update-asf` only commits allowlisted files (never `.github/workflows`).

## Releases

1. Prefer merging an `asf-bump` PR (auto patch bump) **or** manually bump `<Version>` in `ASFBotSocial.csproj`.
2. Push/merge to `main` → `release-on-bump` builds, verifies bind, then creates `vX.Y.Z` if missing and publishes ZIP.
3. Duplicate release is skipped; tag pointing at a different commit is refused; tag is never created before a green build.

Keep older GitHub Releases so users on older ASF builds can download a matching plugin DLL.
