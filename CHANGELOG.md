# Changelog

All notable changes to **ASFBotSocial** are documented in this file.

### [1.1.50] - 2026-08-14

- **Wishlist:** `EndpointRateLimiter` on `Wishlist/Add` and `Wishlist/Remove` (3s).
- **Repo layout:** services grouped by domain (`Friends/`, `Games/`, `Inventory/`, …); removed scratch `tmp-*` / dead `IpcConfig`.
- **CI / hooks:** GitHub Actions build + release workflows; local `.githooks` + `scripts/validate.ps1`.

### [1.1.49] - 2026-08-14

- **Friends:** rate limits on `Friends/Add` (4s) and `Friends/Remove` (3s).

### Earlier

See git history for prior IPC endpoints (games, discovery queue, shared files, curators, reviews, inventory transfer).
