using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ASFBotSocial.Models;
using SteamKit2;
using SteamKit2.Internal;

namespace ASFBotSocial.Services;

internal sealed class AchievementsService {
	private readonly RateLimiter readLimiter = new(TimeSpan.FromSeconds(2));
	private readonly RateLimiter mutateLimiter = new(TimeSpan.FromSeconds(4));

	public async Task<GameAchievementsResponse?> ListAsync(Bot bot, uint appId, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (appId == 0) {
			return null;
		}

		await readLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		AchievementHandler? handler = AchievementHandler.For(bot);

		if ((handler == null) || !bot.IsConnectedAndLoggedOn) {
			return null;
		}

		CMsgClientGetUserStatsResponse? raw = await handler.GetUserStatsAsync(bot, appId).ConfigureAwait(false);

		if (raw == null) {
			return null;
		}

		List<AchievementHandler.AchievementStat>? parsed = AchievementHandler.ParseAchievements(raw);

		if (parsed == null) {
			return null;
		}

		List<Meta> publicMetaList = await TryLoadPublicMetaListAsync(bot, appId).ConfigureAwait(false);
		Dictionary<string, Meta> metaByApi = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Meta> metaByName = new(StringComparer.OrdinalIgnoreCase);

		foreach (Meta meta in publicMetaList) {
			if (!string.IsNullOrEmpty(meta.ApiName)) {
				metaByApi[meta.ApiName] = meta;
			}

			if (!string.IsNullOrEmpty(meta.Name)) {
				metaByName[NormalizeKey(meta.Name)] = meta;
			}
		}

		List<AchievementEntry> items = new(parsed.Count);
		uint unlocked = 0;

		for (int i = 0; i < parsed.Count; i++) {
			AchievementHandler.AchievementStat row = parsed[i];

			if (row.IsSet) {
				unlocked++;
			}

			Meta? publicMeta = null;

			if (!string.IsNullOrEmpty(row.ApiName) && metaByApi.TryGetValue(row.ApiName, out Meta? byApi)) {
				publicMeta = byApi;
			} else if (metaByName.TryGetValue(NormalizeKey(row.Name), out Meta? byName)) {
				publicMeta = byName;
			} else if ((i < publicMetaList.Count) && (publicMetaList.Count == parsed.Count)) {
				// Same count → align by order when API names differ.
				publicMeta = publicMetaList[i];
			}

			string name = !string.IsNullOrWhiteSpace(publicMeta?.Name) ? publicMeta.Name! : row.Name;
			string description = !string.IsNullOrWhiteSpace(publicMeta?.Description) ? publicMeta.Description! : row.Description;
			string? unlockedIcon = ResolveIconUrl(appId, publicMeta?.Icon, row.Icon);
			string? lockedIcon = ResolveIconUrl(appId, publicMeta?.IconGray, row.IconGray ?? row.Icon);

			items.Add(
				new AchievementEntry {
					Index = row.Index,
					ApiName = row.ApiName,
					Name = name,
					Description = description,
					IconUrl = row.IsSet ? (unlockedIcon ?? lockedIcon) : (lockedIcon ?? unlockedIcon),
					Unlocked = row.IsSet,
					Restricted = row.Restricted,
					Unlockable = !row.Restricted,
				}
			);
		}

		string gameName = await ResolveGameNameAsync(bot, appId).ConfigureAwait(false);

		return new GameAchievementsResponse {
			AppId = appId,
			Name = gameName,
			HeaderImage = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId.ToString(CultureInfo.InvariantCulture) + "/header.jpg",
			Unlocked = unlocked,
			Total = (uint) items.Count,
			Achievements = items,
		};
	}

	public async Task<AchievementMutationResponse> SetAsync(
		Bot bot,
		uint appId,
		IReadOnlyCollection<uint>? indices,
		bool unlockAll,
		bool unlock,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull(bot);

		AchievementMutationResponse fail(string message) => new() {
			Success = false,
			AppId = appId,
			Message = message,
			Changed = 0,
		};

		if (appId == 0) {
			return fail("Invalid AppID");
		}

		await mutateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		AchievementHandler? handler = AchievementHandler.For(bot);

		if ((handler == null) || !bot.IsConnectedAndLoggedOn) {
			return fail("Bot is not connected");
		}

		CMsgClientGetUserStatsResponse? raw = await handler.GetUserStatsAsync(bot, appId).ConfigureAwait(false);

		if (raw == null) {
			return fail("Could not load achievements");
		}

		List<AchievementHandler.AchievementStat>? parsed = AchievementHandler.ParseAchievements(raw);

		if ((parsed == null) || (parsed.Count == 0)) {
			return fail("No achievements for this game");
		}

		HashSet<uint> wanted = new();

		if (unlockAll) {
			wanted.UnionWith(parsed.Where(static row => !row.Restricted).Select(static row => row.Index));
		} else if (indices != null) {
			wanted.UnionWith(indices.Where(static index => index > 0));
		}

		if (wanted.Count == 0) {
			return fail("No achievements selected");
		}

		List<CMsgClientStoreUserStats2.Stats> statsToSet = [];
		List<string> notes = [];
		uint planned = 0;

		foreach (uint index in wanted.OrderBy(static value => value)) {
			AchievementHandler.AchievementStat? row = parsed.Find(item => item.Index == index);

			if (row == null) {
				notes.Add("#" + index.ToString(CultureInfo.InvariantCulture) + " out of range");

				continue;
			}

			if (row.Restricted) {
				notes.Add("#" + index.ToString(CultureInfo.InvariantCulture) + " protected (server-side)");

				continue;
			}

			if (row.IsSet == unlock) {
				notes.Add("#" + index.ToString(CultureInfo.InvariantCulture) + (unlock ? " already unlocked" : " already locked"));

				continue;
			}

			statsToSet.AddRange(AchievementHandler.BuildStatsToSet(statsToSet, row, unlock));
			planned++;
		}

		if (statsToSet.Count == 0) {
			return new AchievementMutationResponse {
				Success = false,
				AppId = appId,
				Changed = 0,
				Message = notes.Count > 0 ? string.Join("; ", notes) : "Nothing to change",
			};
		}

		bool ok = await handler.StoreUserStatsAsync(bot, appId, raw.crc_stats, statsToSet).ConfigureAwait(false);

		return new AchievementMutationResponse {
			Success = ok,
			AppId = appId,
			Changed = ok ? planned : 0,
			Message = ok
				? (unlock ? "Unlocked " : "Locked ") + planned.ToString(CultureInfo.InvariantCulture)
				: "Steam rejected the stats update" + (notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : ""),
		};
	}

	private static async Task<string> ResolveGameNameAsync(Bot bot, uint appId) {
		try {
			Dictionary<uint, string>? owned = await bot.ArchiHandler.GetOwnedGames(bot.SteamID).ConfigureAwait(false);

			if ((owned != null) && owned.TryGetValue(appId, out string? name) && !string.IsNullOrWhiteSpace(name)) {
				return name;
			}
		} catch (Exception) {
			// fall through
		}

		return "App " + appId.ToString(CultureInfo.InvariantCulture);
	}

	private static async Task<List<Meta>> TryLoadPublicMetaListAsync(Bot bot, uint appId) {
		foreach (string language in new[] { "spanish", "english" }) {
			List<Meta> list = await TryLoadPublicMetaForLanguageAsync(bot, appId, language).ConfigureAwait(false);

			if (list.Count > 0) {
				return list;
			}
		}

		return [];
	}

	private static async Task<List<Meta>> TryLoadPublicMetaForLanguageAsync(Bot bot, uint appId, string language) {
		List<Meta> list = [];

		try {
			SteamUnifiedMessages? unified = bot.GetHandler<SteamUnifiedMessages>();

			if (unified == null) {
				return list;
			}

			Player player = unified.CreateService<Player>();
			CPlayer_GetGameAchievements_Request request = new() {
				appid = appId,
				language = language,
			};

			SteamUnifiedMessages.ServiceMethodResponse<CPlayer_GetGameAchievements_Response> response =
				await player.GetGameAchievements(request).ToLongRunningTask().ConfigureAwait(false);

			if (response.Result != EResult.OK) {
				return list;
			}

			foreach (CPlayer_GetGameAchievements_Response.Achievement row in response.Body.achievements) {
				list.Add(
					new Meta {
						ApiName = row.internal_name,
						Name = row.localized_name,
						Description = row.localized_desc ?? "",
						Icon = row.icon,
						IconGray = row.icon_gray,
					}
				);
			}
		} catch (Exception) {
			// Schema names/icons are enough as fallback.
		}

		return list;
	}

	private static string? ResolveIconUrl(uint appId, string? preferred, string? fallback) =>
		BuildIconUrl(appId, preferred) ?? BuildIconUrl(appId, fallback);

	private static string? BuildIconUrl(uint appId, string? icon) {
		if (string.IsNullOrWhiteSpace(icon)) {
			return null;
		}

		string value = icon.Trim();

		if (value.StartsWith("//", StringComparison.Ordinal)) {
			return "https:" + value;
		}

		if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
			return value;
		}

		// Already a community relative path: steamcommunity/public/images/apps/...
		if (value.Contains("steamcommunity/public/images/apps/", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("/public/images/apps/", StringComparison.OrdinalIgnoreCase)) {
			string relative = value.TrimStart('/');

			return "https://cdn.cloudflare.steamstatic.com/" + relative;
		}

		string file = value.TrimStart('/');

		// Strip accidental "apps/{appid}/" prefix from schema values.
		string appPrefix = "apps/" + appId.ToString(CultureInfo.InvariantCulture) + "/";

		if (file.StartsWith(appPrefix, StringComparison.OrdinalIgnoreCase)) {
			file = file[appPrefix.Length..];
		}

		if (!file.Contains('.', StringComparison.Ordinal)) {
			file += ".jpg";
		}

		return "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/"
			+ appId.ToString(CultureInfo.InvariantCulture)
			+ "/"
			+ file;
	}

	private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();

	private sealed class Meta {
		internal string? ApiName { get; init; }
		internal string? Name { get; init; }
		internal string? Description { get; init; }
		internal string? Icon { get; init; }
		internal string? IconGray { get; init; }
	}
}
