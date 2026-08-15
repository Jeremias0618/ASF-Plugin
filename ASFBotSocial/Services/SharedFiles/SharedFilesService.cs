using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using ASFBotSocial.Models;
using SteamKit2;

namespace ASFBotSocial.Services;

internal sealed class SharedFilesService {
	private static readonly Regex SharedFileUrlRegex = new(
		@"steamcommunity\.com/sharedfiles/filedetails/\?id=(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex SharedFileIdLooseRegex = new(
		@"(?:^|[?&]id=)(\d{6,})",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex AppIdRegex = new(
		@"ShowSharePublishedFilePopup\(\s*'?\d+'?\s*,\s*'(\d+)'",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex AppIdFallbackRegex = new(
		@"[""']appid[""']\s*[:=]\s*[""']?(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(2));

	public async Task<MutationsResponse> ActAsync(
		Bot bot,
		string url,
		string? vote,
		bool favorite,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull(bot);

		string trimmed = (url ?? "").Trim();
		string voteKey = (vote ?? "").Trim().ToLowerInvariant();

		if (string.IsNullOrEmpty(trimmed)) {
			return Single(false, "", "Empty URL");
		}

		if (string.IsNullOrEmpty(voteKey) && !favorite) {
			return Single(false, trimmed, "Select like, dislike, and/or favorite");
		}

		if (!string.IsNullOrEmpty(voteKey) && voteKey is not ("like" or "dislike")) {
			return Single(false, trimmed, "Vote must be like or dislike");
		}

		if (!TryParseSharedFileId(trimmed, out ulong fileId)) {
			return Single(false, trimmed, "Invalid shared file URL");
		}

		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			if (!bot.IsConnectedAndLoggedOn) {
				return Single(false, trimmed, "Bot is not connected");
			}

			string target = fileId.ToString(CultureInfo.InvariantCulture);
			List<string> notes = [];

			if (!string.IsNullOrEmpty(voteKey)) {
				bool voteOk = await VoteAsync(bot, fileId, voteKey == "like").ConfigureAwait(false);

				if (!voteOk) {
					return Single(false, target, "Vote failed (Steam rejected the request)");
				}

				notes.Add(voteKey);
			}

			if (favorite) {
				uint? appId = await ResolveAppIdAsync(bot, fileId).ConfigureAwait(false);

				if (appId is null or 0) {
					return Single(false, target, "Could not resolve appId for favorite");
				}

				bool favOk = await FavoriteAsync(bot, fileId, appId.Value).ConfigureAwait(false);

				if (!favOk) {
					return Single(false, target, notes.Count > 0
						? "Voted OK but favorite failed"
						: "Favorite failed (Steam rejected the request)");
				}

				notes.Add("favorite");
			}

			return Single(true, target, "OK — " + string.Join("+", notes));
		} catch (Exception e) {
			return Single(false, trimmed, e.Message);
		}
	}

	internal static bool TryParseSharedFileId(string value, out ulong fileId) {
		fileId = 0;
		string trimmed = value.Trim();

		Match match = SharedFileUrlRegex.Match(trimmed);

		if (!match.Success) {
			match = SharedFileIdLooseRegex.Match(trimmed);
		}

		if (!match.Success) {
			return ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId) && fileId > 0;
		}

		return ulong.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId) && fileId > 0;
	}

	private static async Task<uint?> ResolveAppIdAsync(Bot bot, ulong fileId) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/sharedfiles/filedetails/?id={fileId}");
		BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

		if (response?.Content == null || response.Content.Count == 0) {
			return null;
		}

		string html = Encoding.UTF8.GetString([.. response.Content]);
		Match match = AppIdRegex.Match(html);

		if (!match.Success) {
			match = AppIdFallbackRegex.Match(html);
		}

		if (!match.Success) {
			return null;
		}

		return uint.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint appId) && appId > 0
			? appId
			: null;
	}

	private static async Task<bool> VoteAsync(Bot bot, ulong fileId, bool up) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, up ? "/sharedfiles/voteup" : "/sharedfiles/votedown");
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "id", fileId.ToString(CultureInfo.InvariantCulture) },
			{ "json", "1" },
		};

		ObjectResponse<ResultResponse>? response = await bot.ArchiWebHandler
			.UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data)
			.ConfigureAwait(false);

		return response?.Content == null || response.Content.Result == EResult.OK;
	}

	private static async Task<bool> FavoriteAsync(Bot bot, ulong fileId, uint appId) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, "/sharedfiles/favorite");
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "id", fileId.ToString(CultureInfo.InvariantCulture) },
			{ "appid", appId.ToString(CultureInfo.InvariantCulture) },
		};

		// Favorite often returns empty/HTML; treat HTTP success as OK.
		bool ok = await bot.ArchiWebHandler.UrlPostWithSession(request, data: data).ConfigureAwait(false);

		return ok;
	}

	private static MutationsResponse Single(bool success, string target, string message) =>
		new() {
			Results = [
				new MutationResult {
					Success = success,
					Target = target,
					Message = message,
				},
			],
		};
}
