# Changelog

All notable changes to **ASFBotSocial** are documented in this file.

### [Unreleased]

- **OwnReviews:** paged list and detail (capability `reviews.own.read`) plus **edit** (`PUT /OwnReviews/{appId}`, `GET /OwnReviews/{appId}/Editor`, capability `reviews.own.edit`) and **publish** (`POST /OwnReviews`, capability `reviews.own.publish`) for the bot's own reviews. Edit checks ownership server-side, asks for `ConfirmRatingReset` before changing the recommendation, and success is only reported after Steam confirms by read-back; publish answers `409 review_exists` when the product already has a review. **Verified:** editing the text of a real review and restoring it, Unicode round trip, guard errors. **Not verified:** publishing a new review and changing the recommendation of an existing one. Delete is not implemented. `Reviews/Vote` is unchanged.
### [1.1.51] - 2026-10-02

- **ASF target:** bump **6.3.8.4 → 6.3.10.3** (exact strong-name match with current stable ASF).
- **NuGet:** align with ASF 6.3.10.3 — `Microsoft.AspNetCore.OpenApi` / `System.Composition.AttributedModel` **10.0.12**, `Microsoft.OpenApi` **2.12.0**.
- **Versioning:** `ASFTargetVersion` lives only in `Directory.Build.props`; CI/Release read it from there.
- **Automation:** daily `update-asf` workflow opens PRs when [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) publishes a new stable release; `release-on-bump` tags and publishes `ASFBotSocial.zip` after a green build.
- **Hardening:** CI verifies ASF git tag + DLL assembly bind; release tags only after successful build; write workflows never run on untrusted PR code; CODEOWNERS for workflow paths.

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
