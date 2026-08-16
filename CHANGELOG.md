# Changelog

All notable changes to **ASFBotSocial** are documented in this file.

### [2026-08-15] Install CLI

- **README:** `cd` to the ArchiSteamFarm folder and extract `ASFBotSocial.zip` into `plugins/ASFBotSocial/`. (`README.md`, `README-ESP.md`)

### [2026-08-15] Repo hygiene

- **Dependabot:** removed `.github/dependabot.yml` so weekly version-update PRs are no longer opened.
- **OpenApi:** pin `Microsoft.OpenApi` 2.7.5 (GHSA-v5pm-xwqc-g5wc).

### [1.1.50] - 2026-08-14

- **Wishlist:** `EndpointRateLimiter` on `Wishlist/Add` and `Wishlist/Remove` (3s).
- **Repo layout:** services grouped by domain (`Friends/`, `Games/`, `Inventory/`, …); removed scratch `tmp-*` / dead `IpcConfig`.
- **CI / hooks:** GitHub Actions build + release workflows; local `.githooks` + `scripts/validate.ps1`.

### [1.1.49] - 2026-08-14

- **Friends:** rate limits on `Friends/Add` (4s) and `Friends/Remove` (3s).

### Earlier

See git history for prior IPC endpoints (games, discovery queue, shared files, curators, reviews, inventory transfer).
