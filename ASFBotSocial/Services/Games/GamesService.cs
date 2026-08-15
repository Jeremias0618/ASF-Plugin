using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using ASFBotSocial.Models;
using SteamKit2;
using SteamKit2.Internal;

namespace ASFBotSocial.Services;

internal sealed class GamesService {
	private static readonly Regex StoreAppUrl = new(
		@"(?:store\.steampowered\.com/app/|steam://(?:store|run)/)(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	/// <summary>Steam / ASF concurrent play limit (ArchiHandler.MaxGamesPlayedConcurrently).</summary>
	internal const int MaxIdleGames = 32;

	private static readonly ConcurrentDictionary<uint, GameCoverResponse> CoverCache = new();

	private readonly RateLimiter searchLimiter = new(TimeSpan.FromMilliseconds(800));
	private readonly RateLimiter addLimiter = new(TimeSpan.FromSeconds(3));
	private readonly RateLimiter statsLimiter = new(TimeSpan.FromSeconds(2));
	private readonly RateLimiter boosterIdleLimiter = new(TimeSpan.FromSeconds(3));
	private readonly RateLimiter coverLimiter = new(TimeSpan.FromMilliseconds(400));

	public async Task<GamesResponse?> ListAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!bot.IsConnectedAndLoggedOn) {
			return null;
		}

		SteamUnifiedMessages? unified = bot.GetHandler<SteamUnifiedMessages>();

		if (unified == null) {
			return await ListOwnedFallbackAsync(bot).ConfigureAwait(false);
		}

		try {
			// SteamKit unified calls must stay sequential on the same connection.
			Dictionary<uint, MutableGameEntry> owned = await LoadOwnedGamesAsync(unified, bot.SteamID).ConfigureAwait(false);

			if (owned.Count == 0) {
				owned = await LoadOwnedGamesFallbackAsync(bot).ConfigureAwait(false);
			}

			Dictionary<uint, MutableGameEntry> shared = await LoadSharedLibraryGamesAsync(unified, bot).ConfigureAwait(false);

			if ((owned.Count == 0) && (shared.Count == 0)) {
				return null;
			}

			Dictionary<uint, MutableGameEntry> merged = new(owned.Count + shared.Count);

			foreach ((uint appId, MutableGameEntry entry) in owned) {
				merged[appId] = entry;
			}

			foreach ((uint appId, MutableGameEntry entry) in shared) {
				if (merged.TryGetValue(appId, out MutableGameEntry? existing)) {
					existing.IsShared = true;

					if (!string.IsNullOrWhiteSpace(entry.Name) && (existing.Name.StartsWith("App ", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(existing.Name))) {
						existing.Name = entry.Name;
					}

					if ((existing.AppType == "game") && (entry.AppType != "game") && (entry.AppType != "other")) {
						existing.AppType = entry.AppType;
					}
				} else {
					merged[appId] = entry;
				}
			}

			// has_community_visible_stats is unreliable; mirror Stats and query progress.
			Player player = unified.CreateService<Player>();
			Dictionary<uint, (uint Unlocked, uint Total)> progress = await GetAchievementsProgressAsync(
				player,
				bot.SteamID,
				merged.Keys.ToList(),
				cancellationToken
			).ConfigureAwait(false);

			foreach ((uint appId, (uint _, uint total)) in progress) {
				if ((total > 0) && merged.TryGetValue(appId, out MutableGameEntry? entry)) {
					entry.HasAchievements = true;
				}
			}

			HashSet<uint> cardAppIds = await LoadCardAppIdsAsync(bot).ConfigureAwait(false);

			foreach (MutableGameEntry entry in merged.Values) {
				entry.HasCards = cardAppIds.Contains(entry.AppId);
			}

			List<GameEntry> games = merged.Values
				.Select(static entry => entry.ToEntry())
				.OrderBy(static game => game.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return new GamesResponse {
				Total = games.Count,
				OwnedTotal = games.Count(static g => g.IsOwned),
				SharedTotal = games.Count(static g => g.IsShared && !g.IsOwned),
				Games = games,
			};
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Games list failed: " + e);

			return await ListOwnedFallbackAsync(bot).ConfigureAwait(false);
		}
	}

	public async Task<GameSearchResponse?> SearchAsync(Bot bot, string query, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		string trimmed = (query ?? "").Trim();

		if (string.IsNullOrWhiteSpace(trimmed)) {
			return new GameSearchResponse {
				Query = "",
				Total = 0,
				Items = Array.Empty<GameSearchHit>(),
			};
		}

		await searchLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			HashSet<uint> ownedIds = await GetOwnedAppIdsAsync(bot).ConfigureAwait(false);
			uint? directAppId = TryParseAppId(trimmed);
			List<GameSearchHit> items;

			if (directAppId.HasValue) {
				uint appId = directAppId.Value;
				GameSearchHit? hit = await ResolveAppAsync(bot, appId).ConfigureAwait(false);
				items = new List<GameSearchHit>(1) { hit ?? SyntheticHit(appId) };
			} else {
				items = await StoreSearchAsync(bot, trimmed).ConfigureAwait(false);
			}

			for (int i = 0; i < items.Count; i++) {
				items[i].Owned = ownedIds.Contains(items[i].AppId);
			}

			await EnrichSearchHitsWithDemosAsync(bot, items, ownedIds, cancellationToken).ConfigureAwait(false);

			return new GameSearchResponse {
				Query = trimmed,
				Total = items.Count,
				Items = items,
			};
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Games search failed: " + e);

			uint? fallbackAppId = TryParseAppId(trimmed);

			if (fallbackAppId.HasValue) {
				return new GameSearchResponse {
					Query = trimmed,
					Total = 1,
					Items = new[] { SyntheticHit(fallbackAppId.Value) },
				};
			}

			return null;
		}
	}

	public async Task<GameStatsResponse?> StatsAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		await statsLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		if (!bot.IsConnectedAndLoggedOn) {
			return null;
		}

		SteamUnifiedMessages? unified = bot.GetHandler<SteamUnifiedMessages>();

		if (unified == null) {
			return null;
		}

		try {
			Player player = unified.CreateService<Player>();
			CPlayer_GetOwnedGames_Request request = new() {
				steamid = bot.SteamID,
				include_appinfo = true,
				include_free_sub = true,
				include_played_free_games = true,
				skip_unvetted_apps = false,
			};

			SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetOwnedGames_Response> response =
				await player.GetOwnedGames(request).ToLongRunningTask().ConfigureAwait(false);

			if (response.Result != EResult.OK) {
				return null;
			}

			Dictionary<uint, MutableStatsEntry> merged = new();
			double totalMinutes = 0;
			int played = 0;

			foreach (CPlayer_GetOwnedGames_Response.Game game in response.Body.games) {
				uint appId = (uint) game.appid;

				if (appId == 0) {
					continue;
				}

				uint playtimeMinutes = game.playtime_forever > 0 ? (uint) game.playtime_forever : 0u;
				uint lastPlayed = game.rtime_last_played > 0 ? (uint) game.rtime_last_played : 0u;
				string name = string.IsNullOrWhiteSpace(game.name)
					? ("App " + appId.ToString(CultureInfo.InvariantCulture))
					: game.name;

				totalMinutes += playtimeMinutes;

				if (playtimeMinutes > 0) {
					played++;
				}

				merged[appId] = new MutableStatsEntry {
					AppId = appId,
					Name = name,
					PlaytimeMinutes = playtimeMinutes,
					LastPlayedUnix = lastPlayed,
					IsOwned = true,
				};
			}

			Dictionary<uint, MutableGameEntry> shared = await LoadSharedLibraryGamesAsync(unified, bot).ConfigureAwait(false);

			foreach ((uint appId, MutableGameEntry sharedEntry) in shared) {
				if (merged.TryGetValue(appId, out MutableStatsEntry? existing)) {
					existing.IsShared = true;

					if (!string.IsNullOrWhiteSpace(sharedEntry.Name) && existing.Name.StartsWith("App ", StringComparison.Ordinal)) {
						existing.Name = sharedEntry.Name;
					}

					continue;
				}

				uint playtimeMinutes = sharedEntry.PlaytimeMinutes;
				uint lastPlayed = sharedEntry.LastPlayedUnix;

				totalMinutes += playtimeMinutes;

				if (playtimeMinutes > 0) {
					played++;
				}

				merged[appId] = new MutableStatsEntry {
					AppId = appId,
					Name = sharedEntry.Name,
					PlaytimeMinutes = playtimeMinutes,
					LastPlayedUnix = lastPlayed,
					IsOwned = false,
					IsShared = true,
				};
			}

			Dictionary<uint, (uint Unlocked, uint Total)> achievements = await GetAchievementsProgressAsync(
				player,
				bot.SteamID,
				merged.Keys.ToList(),
				cancellationToken
			).ConfigureAwait(false);

			HashSet<uint> cardAppIds = await LoadCardAppIdsAsync(bot).ConfigureAwait(false);

			List<GameStatsEntry> games = new(merged.Count);

			foreach (MutableStatsEntry entry in merged.Values) {
				uint? unlocked = null;
				uint? total = null;

				if (achievements.TryGetValue(entry.AppId, out (uint Unlocked, uint Total) progress) && (progress.Total > 0)) {
					unlocked = progress.Unlocked;
					total = progress.Total;
				}

				games.Add(
					new GameStatsEntry {
						AppId = entry.AppId,
						Name = entry.Name,
						PlaytimeMinutes = entry.PlaytimeMinutes,
						LastPlayedUnix = entry.LastPlayedUnix,
						HeaderImage = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + entry.AppId.ToString(CultureInfo.InvariantCulture) + "/header.jpg",
						AchievementsUnlocked = unlocked,
						AchievementsTotal = total,
						IsOwned = entry.IsOwned,
						IsShared = entry.IsShared,
						HasCards = cardAppIds.Contains(entry.AppId),
					}
				);
			}

			games.Sort(static (a, b) => {
				int byPlay = b.PlaytimeMinutes.CompareTo(a.PlaytimeMinutes);

				return byPlay != 0 ? byPlay : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			});

			return new GameStatsResponse {
				TotalPlaytimeHours = Math.Round(totalMinutes / 60.0, 1),
				InCollection = games.Count,
				Played = played,
				NeverPlayed = Math.Max(0, games.Count - played),
				Games = games,
			};
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Games stats failed: " + e);

			return null;
		}
	}

	/// <summary>
	/// Steam "Booster pack eligibility" apps, ranked by lifetime playtime for GamesPlayedWhileIdle (max 32).
	/// </summary>
	public async Task<BoosterIdleSuggestionsResponse?> BoosterIdleSuggestionsAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		await boosterIdleLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		if (!bot.IsConnectedAndLoggedOn) {
			return null;
		}

		HashSet<uint> eligible;

		try {
			HashSet<uint>? fromSteam = await bot.ArchiWebHandler.GetBoosterEligibility().ConfigureAwait(false);

			if (fromSteam == null) {
				return null;
			}

			eligible = [];

			foreach (uint appId in fromSteam) {
				if (appId > 0) {
					eligible.Add(appId);
				}
			}
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Booster eligibility failed: " + e);

			return null;
		}

		if (eligible.Count == 0) {
			return new BoosterIdleSuggestionsResponse {
				EligibleTotal = 0,
				SelectedTotal = 0,
				MaxIdle = MaxIdleGames,
				Games = [],
				Pool = [],
			};
		}

		Dictionary<uint, (string Name, uint PlaytimeMinutes)> playtimes = await LoadPlaytimeByAppIdAsync(bot).ConfigureAwait(false);

		List<BoosterIdleSuggestionEntry> pool = eligible
			.Select(appId => {
				playtimes.TryGetValue(appId, out (string Name, uint PlaytimeMinutes) info);
				string name = string.IsNullOrWhiteSpace(info.Name)
					? ("App " + appId.ToString(CultureInfo.InvariantCulture))
					: info.Name;

				return new BoosterIdleSuggestionEntry {
					AppId = appId,
					Name = name,
					PlaytimeMinutes = info.PlaytimeMinutes,
				};
			})
			.OrderByDescending(static entry => entry.PlaytimeMinutes)
			.ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		List<BoosterIdleSuggestionEntry> selected = pool.Take(MaxIdleGames).ToList();

		return new BoosterIdleSuggestionsResponse {
			EligibleTotal = eligible.Count,
			SelectedTotal = selected.Count,
			MaxIdle = MaxIdleGames,
			Games = selected,
			Pool = pool,
		};
	}

	/// <summary>
	/// Resolves hashed store artwork URLs for apps where classic CDN paths 404 (common for demos / new apps).
	/// </summary>
	public async Task<GameCoverResponse?> ResolveCoverAsync(Bot bot, uint appId, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (appId == 0) {
			return null;
		}

		if (CoverCache.TryGetValue(appId, out GameCoverResponse? cached)) {
			return cached;
		}

		await coverLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		if (CoverCache.TryGetValue(appId, out cached)) {
			return cached;
		}

		StoreAppMeta meta = await GetStoreAppMetaAsync(bot, appId).ConfigureAwait(false);
		string? header = PreferHttpUrl(meta.HeaderImage);
		string? capsule = PreferHttpUrl(meta.CapsuleImage) ?? header;

		if (string.IsNullOrEmpty(header) && string.IsNullOrEmpty(capsule)) {
			return new GameCoverResponse { AppId = appId };
		}

		GameCoverResponse cover = new() {
			AppId = appId,
			HeaderImage = header,
			CapsuleImage = capsule,
		};

		CoverCache[appId] = cover;

		return cover;
	}

	public async Task<MutationsResponse> AddAsync(Bot bot, IReadOnlyCollection<uint> appIds, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(appIds);

		List<MutationResult> results = new();
		HashSet<uint> ownedIds = await GetOwnedAppIdsAsync(bot).ConfigureAwait(false);

		foreach (uint appId in appIds.Distinct()) {
			await addLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			if (appId == 0) {
				results.Add(new MutationResult { Success = false, Target = "0", Message = "Invalid AppID" });

				continue;
			}

			if (ownedIds.Contains(appId)) {
				results.Add(
					new MutationResult {
						Success = true,
						Target = appId.ToString(CultureInfo.InvariantCulture),
						Message = "Already owned",
					}
				);

				continue;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				results.Add(
					new MutationResult {
						Success = false,
						Target = appId.ToString(CultureInfo.InvariantCulture),
						Message = "Bot is not connected",
					}
				);

				continue;
			}

			try {
				MutationResult added = await TryAddFreeLicenseAsync(bot, appId).ConfigureAwait(false);

				if (added.Success) {
					ownedIds.Add(appId);
					results.Add(added);

					continue;
				}

				// Full game not grantable (unreleased / paid): claim linked free demo when present.
				MutationResult? demoAdded = await TryAddLinkedDemoAsync(bot, appId, ownedIds).ConfigureAwait(false);

				if (demoAdded != null) {
					results.Add(demoAdded);

					continue;
				}

				results.Add(added);
			} catch (Exception e) {
				results.Add(
					new MutationResult {
						Success = false,
						Target = appId.ToString(CultureInfo.InvariantCulture),
						Message = e.Message,
					}
				);
			}
		}

		return new MutationsResponse { Results = results };
	}

	internal static uint? TryParseAppId(string raw) {
		string value = (raw ?? "").Trim();

		if (string.IsNullOrEmpty(value)) {
			return null;
		}

		Match urlMatch = StoreAppUrl.Match(value);

		if (urlMatch.Success && uint.TryParse(urlMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint fromUrl) && (fromUrl > 0)) {
			return fromUrl;
		}

		if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint direct) && (direct > 0)) {
			return direct;
		}

		return null;
	}

	private static async Task<HashSet<uint>> GetOwnedAppIdsAsync(Bot bot) {
		try {
			Dictionary<uint, string>? owned = await bot.ArchiHandler.GetOwnedGames(bot.SteamID).ConfigureAwait(false);

			return owned == null ? new HashSet<uint>() : new HashSet<uint>(owned.Keys);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Owned games lookup failed: " + e.Message);

			return new HashSet<uint>();
		}
	}

	private static async Task<List<GameSearchHit>> StoreSearchAsync(Bot bot, string term) {
		string url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(term) + "&l=spanish&cc=US";
		JsonNode? root = await GetStoreJsonAsync(bot, url).ConfigureAwait(false);
		JsonArray? items = root?["items"] as JsonArray;

		if (items == null) {
			return new List<GameSearchHit>();
		}

		List<GameSearchHit> hits = new();

		foreach (JsonNode? node in items) {
			GameSearchHit? hit = MapStoreItem(node);

			if (hit != null) {
				hits.Add(hit);
			}
		}

		return hits;
	}

	private static async Task<MutationResult> TryAddFreeLicenseAsync(Bot bot, uint appId) {
		string target = appId.ToString(CultureInfo.InvariantCulture);

		(EResult appResult, IReadOnlyCollection<uint>? grantedApps, IReadOnlyCollection<uint>? grantedPackages) =
			await bot.Actions.AddFreeLicenseApp(appId).ConfigureAwait(false);

		bool appGranted = ((grantedApps != null) && (grantedApps.Count > 0)) || ((grantedPackages != null) && (grantedPackages.Count > 0));

		if ((appResult == EResult.OK) && appGranted) {
			return new MutationResult { Success = true, Target = target, Message = "OK" };
		}

		// 100% promos / limited free packages need AddFreeLicensePackage(subID), not RequestFreeLicense(app).
		List<uint> freePackages = await GetFreePackageIdsAsync(bot, appId).ConfigureAwait(false);

		if (freePackages.Count == 0) {
			return new MutationResult {
				Success = false,
				Target = target,
				Message = appResult.ToString() + " (not free / not grantable — only free licenses can be added)",
			};
		}

		List<string> packageNotes = new();

		foreach (uint subId in freePackages) {
			if (bot.OwnedPackages.ContainsKey(subId)) {
				return new MutationResult { Success = true, Target = target, Message = "Already owned" };
			}

			(EResult packageResult, EPurchaseResultDetail purchaseDetail) =
				await bot.Actions.AddFreeLicensePackage(subId).ConfigureAwait(false);

			string note = "sub/" + subId.ToString(CultureInfo.InvariantCulture) + "=" + packageResult + "/" + purchaseDetail;
			packageNotes.Add(note);

			bool claimed = (packageResult == EResult.OK) || (purchaseDetail == EPurchaseResultDetail.AlreadyPurchased);

			if (claimed) {
				HashSet<uint> ownedAfter = await GetOwnedAppIdsAsync(bot).ConfigureAwait(false);

				if (ownedAfter.Contains(appId) || (purchaseDetail == EPurchaseResultDetail.AlreadyPurchased) || (packageResult == EResult.OK)) {
					return new MutationResult {
						Success = true,
						Target = target,
						Message = purchaseDetail == EPurchaseResultDetail.AlreadyPurchased ? "Already owned" : "OK via " + note,
					};
				}
			}
		}

		return new MutationResult {
			Success = false,
			Target = target,
			Message = "Free package claim failed (" + string.Join("; ", packageNotes) + ")",
		};
	}

	private static async Task<MutationResult?> TryAddLinkedDemoAsync(Bot bot, uint parentAppId, HashSet<uint> ownedIds) {
		List<uint> demoIds = await GetDemoAppIdsAsync(bot, parentAppId).ConfigureAwait(false);

		if (demoIds.Count == 0) {
			return null;
		}

		List<string> failures = new();

		foreach (uint demoId in demoIds) {
			string demoTarget = demoId.ToString(CultureInfo.InvariantCulture);

			if (ownedIds.Contains(demoId)) {
				return new MutationResult {
					Success = true,
					Target = demoTarget,
					Message = "Already owned (demo " + demoTarget + ")",
				};
			}

			MutationResult demoResult = await TryAddFreeLicenseAsync(bot, demoId).ConfigureAwait(false);

			if (demoResult.Success) {
				ownedIds.Add(demoId);

				return new MutationResult {
					Success = true,
					Target = demoTarget,
					Message = demoResult.Message.StartsWith("Already owned", StringComparison.Ordinal)
						? "Already owned (demo " + demoTarget + ")"
						: "OK via demo " + demoTarget + " (parent " + parentAppId.ToString(CultureInfo.InvariantCulture) + ")",
				};
			}

			failures.Add(demoTarget + ": " + demoResult.Message);
		}

		return new MutationResult {
			Success = false,
			Target = parentAppId.ToString(CultureInfo.InvariantCulture),
			Message = "Demo claim failed (" + string.Join("; ", failures) + ")",
		};
	}

	private static async Task EnrichSearchHitsWithDemosAsync(
		Bot bot,
		List<GameSearchHit> items,
		HashSet<uint> ownedIds,
		CancellationToken cancellationToken
	) {
		const int maxEnrich = 10;
		int limit = Math.Min(items.Count, maxEnrich);

		for (int i = 0; i < limit; i++) {
			cancellationToken.ThrowIfCancellationRequested();
			GameSearchHit hit = items[i];

			if (hit.AppId == 0) {
				continue;
			}

			StoreAppMeta meta = await GetStoreAppMetaAsync(bot, hit.AppId).ConfigureAwait(false);

			if (!string.IsNullOrEmpty(meta.CapsuleImage)) {
				hit.TinyImage = meta.CapsuleImage;
			} else if (!string.IsNullOrEmpty(meta.HeaderImage)) {
				hit.TinyImage = meta.HeaderImage;
			}

			if (!string.IsNullOrEmpty(meta.HeaderImage) || !string.IsNullOrEmpty(meta.CapsuleImage)) {
				CoverCache[hit.AppId] = new GameCoverResponse {
					AppId = hit.AppId,
					HeaderImage = PreferHttpUrl(meta.HeaderImage),
					CapsuleImage = PreferHttpUrl(meta.CapsuleImage) ?? PreferHttpUrl(meta.HeaderImage),
				};
			}

			if (meta.IsDemo) {
				hit.IsDemo = true;

				continue;
			}

			if (meta.DemoAppIds.Count == 0) {
				continue;
			}

			uint demoId = meta.DemoAppIds[0];
			hit.DemoAppId = demoId;
			hit.DemoOwned = ownedIds.Contains(demoId);
		}
	}

	private static async Task<List<uint>> GetDemoAppIdsAsync(Bot bot, uint appId) {
		StoreAppMeta meta = await GetStoreAppMetaAsync(bot, appId).ConfigureAwait(false);

		return meta.DemoAppIds;
	}

	private static async Task<StoreAppMeta> GetStoreAppMetaAsync(Bot bot, uint appId) {
		string url = "https://store.steampowered.com/api/appdetails?appids="
			+ appId.ToString(CultureInfo.InvariantCulture)
			+ "&cc=us&l=english";

		JsonNode? root = await GetStoreJsonAsync(bot, url).ConfigureAwait(false);
		JsonNode? appNode = root?[appId.ToString(CultureInfo.InvariantCulture)];

		if (!ReadBool(appNode?["success"])) {
			return StoreAppMeta.Empty;
		}

		JsonNode? data = appNode?["data"];
		string? type = ReadString(data?["type"]);
		bool isDemo = string.Equals(type, "demo", StringComparison.OrdinalIgnoreCase);
		bool isFree = ReadBool(data?["is_free"]);
		List<uint> demoIds = ParseDemoAppIds(data?["demos"]);
		string? headerImage = PreferHttpUrl(ReadString(data?["header_image"]));
		string? capsuleImage = PreferHttpUrl(ReadString(data?["capsule_image"]))
			?? PreferHttpUrl(ReadString(data?["capsule_imagev5"]));

		return new StoreAppMeta(isDemo, isFree, demoIds, headerImage, capsuleImage);
	}

	private static string? PreferHttpUrl(string? url) {
		if (string.IsNullOrWhiteSpace(url)) {
			return null;
		}

		string trimmed = url.Trim();

		return trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? trimmed : null;
	}

	private static List<uint> ParseDemoAppIds(JsonNode? demosNode) {
		List<uint> ids = new();

		if (demosNode == null) {
			return ids;
		}

		if (demosNode is JsonArray demosArray) {
			foreach (JsonNode? entry in demosArray) {
				uint demoId = ReadDemoAppId(entry);

				if ((demoId > 0) && !ids.Contains(demoId)) {
					ids.Add(demoId);
				}
			}

			return ids;
		}

		// Steam sometimes returns a single demo object instead of an array.
		uint single = ReadDemoAppId(demosNode);

		if (single > 0) {
			ids.Add(single);
		}

		return ids;
	}

	private static uint ReadDemoAppId(JsonNode? node) {
		if (node == null) {
			return 0;
		}

		uint fromProp = ReadUInt(node["appid"]);

		if (fromProp > 0) {
			return fromProp;
		}

		return ReadUInt(node);
	}

	private readonly record struct StoreAppMeta(
		bool IsDemo,
		bool IsFree,
		List<uint> DemoAppIds,
		string? HeaderImage,
		string? CapsuleImage
	) {
		internal static StoreAppMeta Empty { get; } = new(false, false, new List<uint>(), null, null);
	}

	private static async Task<List<uint>> GetFreePackageIdsAsync(Bot bot, uint appId) {
		string url = "https://store.steampowered.com/api/appdetails?appids="
			+ appId.ToString(CultureInfo.InvariantCulture)
			+ "&filters=basic,price_overview,packages&cc=us&l=english";

		JsonNode? root = await GetStoreJsonAsync(bot, url).ConfigureAwait(false);
		JsonNode? appNode = root?[appId.ToString(CultureInfo.InvariantCulture)];

		if (!ReadBool(appNode?["success"])) {
			return new List<uint>();
		}

		JsonNode? data = appNode?["data"];
		HashSet<uint> freeIds = new();

		if (data?["package_groups"] is JsonArray groups) {
			foreach (JsonNode? group in groups) {
				if (group?["subs"] is not JsonArray subs) {
					continue;
				}

				foreach (JsonNode? sub in subs) {
					uint packageId = ReadUInt(sub?["packageid"]);

					if (packageId == 0) {
						continue;
					}

					bool isFreeLicense = ReadBool(sub?["is_free_license"]);
					int? priceCents = ReadInt(sub?["price_in_cents_with_discount"]);

					if (isFreeLicense || (priceCents is 0)) {
						freeIds.Add(packageId);
					}
				}
			}
		}

		// Prefer promo packages first (usually higher package IDs for limited free offers).
		return freeIds.OrderByDescending(static id => id).ToList();
	}

	private static async Task<Dictionary<uint, (uint Unlocked, uint Total)>> GetAchievementsProgressAsync(
		Player player,
		ulong steamId,
		IReadOnlyList<uint> appIds,
		CancellationToken cancellationToken
	) {
		Dictionary<uint, (uint Unlocked, uint Total)> map = new();

		if ((appIds == null) || (appIds.Count == 0)) {
			return map;
		}

		const int batchSize = 50;

		for (int offset = 0; offset < appIds.Count; offset += batchSize) {
			cancellationToken.ThrowIfCancellationRequested();

			CPlayer_GetAchievementsProgress_Request request = new() {
				steamid = steamId,
				language = "english",
				include_unvetted_apps = true,
			};

			int end = Math.Min(offset + batchSize, appIds.Count);

			for (int i = offset; i < end; i++) {
				request.appids.Add(appIds[i]);
			}

			try {
				SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetAchievementsProgress_Response> response =
					await player.GetAchievementsProgress(request).ToLongRunningTask().ConfigureAwait(false);

				if (response.Result != EResult.OK) {
					continue;
				}

				foreach (CPlayer_GetAchievementsProgress_Response.AchievementProgress row in response.Body.achievement_progress) {
					uint appId = row.appid;
					uint total = row.total;
					uint unlocked = row.unlocked;

					if ((appId == 0) || (total == 0)) {
						continue;
					}

					map[appId] = (unlocked, total);
				}
			} catch (Exception) {
				// Keep stats usable even if a batch of achievements fails.
			}
		}

		return map;
	}

	private static async Task<GameSearchHit?> ResolveAppAsync(Bot bot, uint appId) {
		List<GameSearchHit> fromSearch = await StoreSearchAsync(bot, appId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

		for (int i = 0; i < fromSearch.Count; i++) {
			if (fromSearch[i].AppId == appId) {
				return NormalizePriceHit(fromSearch[i]);
			}
		}

		string url = "https://store.steampowered.com/api/appdetails?appids="
			+ appId.ToString(CultureInfo.InvariantCulture)
			+ "&filters=basic,price_overview,packages&cc=us&l=spanish";
		JsonNode? root = await GetStoreJsonAsync(bot, url).ConfigureAwait(false);
		JsonNode? appNode = root?[appId.ToString(CultureInfo.InvariantCulture)];

		if (!ReadBool(appNode?["success"])) {
			return SyntheticHit(appId);
		}

		JsonNode? data = appNode?["data"];
		string name = ReadString(data?["name"]) ?? ("App " + appId.ToString(CultureInfo.InvariantCulture));
		JsonNode? price = data?["price_overview"];
		int? discount = ReadInt(price?["discount_percent"]);
		int? finalPrice = ReadInt(price?["final"]);
		int? initialPrice = ReadInt(price?["initial"]);
		bool isDemo = string.Equals(ReadString(data?["type"]), "demo", StringComparison.OrdinalIgnoreCase);
		bool isFree = ReadBool(data?["is_free"]);
		List<uint> demoIds = ParseDemoAppIds(data?["demos"]);
		string? headerImage = PreferHttpUrl(ReadString(data?["header_image"]));
		string? capsuleImage = PreferHttpUrl(ReadString(data?["capsule_image"]))
			?? PreferHttpUrl(ReadString(data?["capsule_imagev5"]));

		// Steam often keeps final == initial on 100% promos while final_formatted is "Free".
		if ((discount is 100) || HasFreePackage(data) || isFree || isDemo) {
			finalPrice = 0;
			discount ??= 100;
		}

		GameSearchHit hit = new() {
			AppId = appId,
			Name = name,
			TinyImage = capsuleImage ?? headerImage ?? CapsuleUrl(appId),
			Currency = ReadString(price?["currency"]),
			InitialPrice = initialPrice,
			FinalPrice = finalPrice,
			DiscountPercent = discount,
			IsDemo = isDemo,
		};

		if (!string.IsNullOrEmpty(headerImage) || !string.IsNullOrEmpty(capsuleImage)) {
			CoverCache[appId] = new GameCoverResponse {
				AppId = appId,
				HeaderImage = headerImage,
				CapsuleImage = capsuleImage ?? headerImage,
			};
		}

		if (!isDemo && (demoIds.Count > 0)) {
			hit.DemoAppId = demoIds[0];
		}

		return hit;
	}

	private static GameSearchHit NormalizePriceHit(GameSearchHit hit) {
		if ((hit.DiscountPercent is 100) && (hit.FinalPrice is null or > 0)) {
			return new GameSearchHit {
				AppId = hit.AppId,
				Name = hit.Name,
				TinyImage = hit.TinyImage,
				Currency = hit.Currency,
				InitialPrice = hit.InitialPrice,
				FinalPrice = 0,
				DiscountPercent = 100,
				Owned = hit.Owned,
				IsDemo = hit.IsDemo,
				DemoAppId = hit.DemoAppId,
				DemoOwned = hit.DemoOwned,
			};
		}

		return hit;
	}

	private static bool HasFreePackage(JsonNode? data) {
		if (data?["package_groups"] is not JsonArray groups) {
			return false;
		}

		foreach (JsonNode? group in groups) {
			if (group?["subs"] is not JsonArray subs) {
				continue;
			}

			foreach (JsonNode? sub in subs) {
				if (ReadBool(sub?["is_free_license"]) || (ReadInt(sub?["price_in_cents_with_discount"]) is 0)) {
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Uses ASF WebBrowser (same path as SteamIdResolver). HttpClient is trimmed out of ASF single-file host.
	/// </summary>
	private static async Task<JsonNode?> GetStoreJsonAsync(Bot bot, string absoluteUrl) {
		Uri request = new(absoluteUrl);
		BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

		if ((response?.Content == null) || (response.Content.Count == 0)) {
			return null;
		}

		byte[] bytes = [.. response.Content];
		string body = Encoding.UTF8.GetString(bytes);

		if (string.IsNullOrWhiteSpace(body)) {
			return null;
		}

		return JsonNode.Parse(body);
	}

	private static GameSearchHit SyntheticHit(uint appId) =>
		new() {
			AppId = appId,
			Name = "App " + appId.ToString(CultureInfo.InvariantCulture),
			TinyImage = CapsuleUrl(appId),
		};

	private static GameSearchHit? MapStoreItem(JsonNode? node) {
		if (node == null) {
			return null;
		}

		string? type = ReadString(node["type"]);

		if (!string.Equals(type, "app", StringComparison.OrdinalIgnoreCase)) {
			return null;
		}

		uint appId = ReadUInt(node["id"]);

		if (appId == 0) {
			return null;
		}

		string name = ReadString(node["name"]) ?? ("App " + appId.ToString(CultureInfo.InvariantCulture));
		JsonNode? price = node["price"];
		int? discount = ReadInt(price?["discount_percent"]);
		int? finalPrice = ReadInt(price?["final"]);

		if (discount is 100) {
			finalPrice = 0;
		}

		return new GameSearchHit {
			AppId = appId,
			Name = name,
			TinyImage = ReadString(node["tiny_image"]) ?? CapsuleUrl(appId),
			Currency = ReadString(price?["currency"]),
			InitialPrice = ReadInt(price?["initial"]),
			FinalPrice = finalPrice,
			DiscountPercent = discount,
		};
	}

	private static string CapsuleUrl(uint appId) =>
		"https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId.ToString(CultureInfo.InvariantCulture) + "/capsule_231x87.jpg";

	private static string? ReadString(JsonNode? node) {
		if (node == null) {
			return null;
		}

		try {
			JsonValueKind kind = node.GetValueKind();

			if (kind == JsonValueKind.String) {
				return node.GetValue<string>();
			}

			if ((kind == JsonValueKind.Number) || (kind == JsonValueKind.True) || (kind == JsonValueKind.False)) {
				return node.ToString();
			}

			return null;
		} catch (Exception) {
			return node.ToString();
		}
	}

	private static bool ReadBool(JsonNode? node) {
		if (node == null) {
			return false;
		}

		try {
			JsonValueKind kind = node.GetValueKind();

			if (kind == JsonValueKind.True) {
				return true;
			}

			if (kind == JsonValueKind.False) {
				return false;
			}

			if (kind == JsonValueKind.Number) {
				return node.GetValue<long>() != 0;
			}

			if (kind == JsonValueKind.String) {
				return bool.TryParse(node.GetValue<string>(), out bool parsed) && parsed;
			}

			return false;
		} catch (Exception) {
			return false;
		}
	}

	private static uint ReadUInt(JsonNode? node) {
		if (node == null) {
			return 0;
		}

		try {
			JsonValueKind kind = node.GetValueKind();

			if (kind == JsonValueKind.Number) {
				long value = node.GetValue<long>();

				return value > 0 ? (uint) value : 0u;
			}

			if (kind == JsonValueKind.String) {
				return uint.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0u;
			}

			return 0u;
		} catch (Exception) {
			return uint.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint fallback) ? fallback : 0u;
		}
	}

	private static int? ReadInt(JsonNode? node) {
		if (node == null) {
			return null;
		}

		try {
			JsonValueKind kind = node.GetValueKind();

			if (kind == JsonValueKind.Number) {
				return (int) node.GetValue<long>();
			}

			if (kind == JsonValueKind.String) {
				return int.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
			}

			return null;
		} catch (Exception) {
			return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fallback) ? fallback : null;
		}
	}

	private static async Task<Dictionary<uint, MutableGameEntry>> LoadOwnedGamesAsync(SteamUnifiedMessages unified, ulong steamId) {
		Dictionary<uint, MutableGameEntry> map = new();

		try {
			Player player = unified.CreateService<Player>();
			CPlayer_GetOwnedGames_Request request = new() {
				steamid = steamId,
				include_appinfo = true,
				include_extended_appinfo = true,
				include_free_sub = true,
				include_played_free_games = true,
				skip_unvetted_apps = false,
			};

			SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetOwnedGames_Response> response =
				await player.GetOwnedGames(request).ToLongRunningTask().ConfigureAwait(false);

			if (response.Result != EResult.OK) {
				return map;
			}

			foreach (CPlayer_GetOwnedGames_Response.Game game in response.Body.games) {
				uint appId = (uint) game.appid;

				if (appId == 0) {
					continue;
				}

				string name = string.IsNullOrWhiteSpace(game.name)
					? ("App " + appId.ToString(CultureInfo.InvariantCulture))
					: game.name;

				map[appId] = new MutableGameEntry {
					AppId = appId,
					Name = name,
					IsOwned = true,
					AppType = "game",
				};
			}
		} catch (Exception) {
			// Caller may fall back to ArchiHandler.GetOwnedGames.
		}

		return map;
	}

	private static async Task<Dictionary<uint, MutableGameEntry>> LoadOwnedGamesFallbackAsync(Bot bot) {
		Dictionary<uint, MutableGameEntry> map = new();
		Dictionary<uint, string>? owned = await bot.ArchiHandler.GetOwnedGames(bot.SteamID).ConfigureAwait(false);

		if (owned == null) {
			return map;
		}

		foreach ((uint appId, string? name) in owned) {
			map[appId] = new MutableGameEntry {
				AppId = appId,
				Name = string.IsNullOrWhiteSpace(name) ? ("App " + appId.ToString(CultureInfo.InvariantCulture)) : name,
				IsOwned = true,
				AppType = "game",
			};
		}

		return map;
	}

	private static async Task<GamesResponse?> ListOwnedFallbackAsync(Bot bot) {
		Dictionary<uint, MutableGameEntry> owned = await LoadOwnedGamesFallbackAsync(bot).ConfigureAwait(false);

		if (owned.Count == 0) {
			return null;
		}

		List<GameEntry> games = owned.Values
			.Select(static entry => entry.ToEntry())
			.OrderBy(static game => game.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new GamesResponse {
			Total = games.Count,
			OwnedTotal = games.Count,
			SharedTotal = 0,
			Games = games,
		};
	}

	private static async Task<Dictionary<uint, MutableGameEntry>> LoadSharedLibraryGamesAsync(SteamUnifiedMessages unified, Bot bot) {
		Dictionary<uint, MutableGameEntry> map = new();

		try {
			FamilyGroups family = unified.CreateService<FamilyGroups>();
			CFamilyGroups_GetFamilyGroupForUser_Request familyRequest = new() {
				steamid = bot.SteamID,
				include_family_group_response = false,
			};

			SteamUnifiedMessages.ServiceMethodResponse<CFamilyGroups_GetFamilyGroupForUser_Response> familyResponse =
				await family.GetFamilyGroupForUser(familyRequest).ToLongRunningTask().ConfigureAwait(false);

			if (familyResponse.Result != EResult.OK) {
				return map;
			}

			ulong familyGroupId = familyResponse.Body.family_groupid;

			if (familyGroupId == 0) {
				familyGroupId = familyResponse.Body.latest_joined_family_groupid;
			}

			if ((familyGroupId == 0) || familyResponse.Body.is_not_member_of_any_group) {
				return map;
			}

			CFamilyGroups_GetSharedLibraryApps_Request sharedRequest = new() {
				family_groupid = familyGroupId,
				include_own = true,
				include_excluded = false,
				include_non_games = true,
				language = "english",
				steamid = bot.SteamID,
			};

			SteamUnifiedMessages.ServiceMethodResponse<CFamilyGroups_GetSharedLibraryApps_Response> sharedResponse =
				await family.GetSharedLibraryApps(sharedRequest).ToLongRunningTask().ConfigureAwait(false);

			if (sharedResponse.Result != EResult.OK) {
				return map;
			}

			foreach (CFamilyGroups_GetSharedLibraryApps_Response.SharedApp app in sharedResponse.Body.apps) {
				if (app.exclude_reason != ESharedLibraryExcludeReason.k_ESharedLibrary_Included) {
					continue;
				}

				uint appId = app.appid;

				if (appId == 0) {
					continue;
				}

				// Skip licenses that only list this bot as owner (already in owned list).
				if ((app.owner_steamids.Count > 0) && app.owner_steamids.All(owner => owner == bot.SteamID)) {
					continue;
				}

				string name = string.IsNullOrWhiteSpace(app.name)
					? ("App " + appId.ToString(CultureInfo.InvariantCulture))
					: app.name;

				// Steam reports shared playtime in seconds.
				uint playtimeSeconds = app.rt_playtime > 0 ? app.rt_playtime : 0u;
				uint playtimeMinutes = playtimeSeconds / 60u;
				uint lastPlayed = app.rt_last_played > 0 ? app.rt_last_played : 0u;

				map[appId] = new MutableGameEntry {
					AppId = appId,
					Name = name,
					IsOwned = false,
					IsShared = true,
					AppType = MapAppType(app.app_type),
					PlaytimeMinutes = playtimeMinutes,
					LastPlayedUnix = lastPlayed,
				};
			}
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Shared library fetch failed: " + e);
		}

		return map;
	}

	private async Task<Dictionary<uint, (string Name, uint PlaytimeMinutes)>> LoadPlaytimeByAppIdAsync(Bot bot) {
		Dictionary<uint, (string Name, uint PlaytimeMinutes)> map = new();

		SteamUnifiedMessages? unified = bot.GetHandler<SteamUnifiedMessages>();

		if (unified != null) {
			try {
				Player player = unified.CreateService<Player>();
				CPlayer_GetOwnedGames_Request request = new() {
					steamid = bot.SteamID,
					include_appinfo = true,
					include_free_sub = true,
					include_played_free_games = true,
					skip_unvetted_apps = false,
				};

				SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetOwnedGames_Response> response =
					await player.GetOwnedGames(request).ToLongRunningTask().ConfigureAwait(false);

				if (response.Result == EResult.OK) {
					foreach (CPlayer_GetOwnedGames_Response.Game game in response.Body.games) {
						uint appId = (uint) game.appid;

						if (appId == 0) {
							continue;
						}

						string name = string.IsNullOrWhiteSpace(game.name)
							? ("App " + appId.ToString(CultureInfo.InvariantCulture))
							: game.name;
						uint playtimeMinutes = game.playtime_forever > 0 ? (uint) game.playtime_forever : 0u;
						map[appId] = (name, playtimeMinutes);
					}
				}
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarning("Booster idle playtime fetch failed: " + e);
			}
		}

		if (map.Count == 0) {
			try {
				Dictionary<uint, string>? owned = await bot.ArchiHandler.GetOwnedGames(bot.SteamID).ConfigureAwait(false);

				if (owned != null) {
					foreach ((uint appId, string name) in owned) {
						if (appId > 0) {
							map[appId] = (
								string.IsNullOrWhiteSpace(name) ? ("App " + appId.ToString(CultureInfo.InvariantCulture)) : name,
								0u
							);
						}
					}
				}
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarning("Booster idle owned-games fallback failed: " + e);
			}
		}

		return map;
	}

	private static async Task<HashSet<uint>> LoadCardAppIdsAsync(Bot bot) {
		HashSet<uint> appIds = [];

		try {
			// Booster creator lists owned apps that have Steam trading cards.
			ImmutableHashSet<BoosterCreatorEntry>? boosterGames = await bot.ArchiWebHandler.GetBoosterCreatorEntries().ConfigureAwait(false);

			if (boosterGames != null) {
				foreach (BoosterCreatorEntry entry in boosterGames) {
					if (entry.AppID > 0) {
						appIds.Add(entry.AppID);
					}
				}
			}

			HashSet<uint>? eligible = await bot.ArchiWebHandler.GetBoosterEligibility().ConfigureAwait(false);

			if (eligible != null) {
				foreach (uint appId in eligible) {
					if (appId > 0) {
						appIds.Add(appId);
					}
				}
			}
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Trading cards scan failed: " + e);
		}

		return appIds;
	}

	private static string MapAppType(EProtoAppType type) {
		int value = (int) type;

		if ((value & (int) EProtoAppType.k_EAppTypeDLC) != 0) {
			return "dlc";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeDemo) != 0) {
			return "demo";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeBeta) != 0) {
			return "beta";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeApplication) != 0) {
			return "application";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeTool) != 0) {
			return "tool";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeVideo) != 0) {
			return "video";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeMusicAlbum) != 0) {
			return "music";
		}

		if ((value & (int) EProtoAppType.k_EAppTypeGame) != 0) {
			return "game";
		}

		return "other";
	}

	private sealed class MutableGameEntry {
		public uint AppId { get; init; }
		public string Name { get; set; } = "";
		public bool IsOwned { get; set; }
		public bool IsShared { get; set; }
		public bool HasAchievements { get; set; }
		public bool HasCards { get; set; }
		public string AppType { get; set; } = "game";
		public uint PlaytimeMinutes { get; set; }
		public uint LastPlayedUnix { get; set; }

		public GameEntry ToEntry() => new() {
			AppId = AppId,
			Name = Name,
			IsOwned = IsOwned,
			IsShared = IsShared,
			HasAchievements = HasAchievements,
			HasCards = HasCards,
			AppType = AppType,
		};
	}

	private sealed class MutableStatsEntry {
		public uint AppId { get; init; }
		public string Name { get; set; } = "";
		public uint PlaytimeMinutes { get; set; }
		public uint LastPlayedUnix { get; set; }
		public bool IsOwned { get; set; }
		public bool IsShared { get; set; }
	}
}
