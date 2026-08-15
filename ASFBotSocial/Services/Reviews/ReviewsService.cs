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

internal sealed class ReviewsService {
	private static readonly Regex ReviewUrlRegex = new(
		@"steamcommunity\.com/(?:id|profiles)/([^/?#]+)/recommended/(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex ReviewIdRegex = new(
		@"RecommendationVoteUpBtn(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(2));

	public async Task<MutationsResponse> VoteAsync(Bot bot, string url, string vote, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		string trimmed = (url ?? "").Trim();
		string voteKey = (vote ?? "").Trim().ToLowerInvariant();

		if (string.IsNullOrEmpty(trimmed)) {
			return Single(false, "", "Empty URL");
		}

		if (voteKey is not ("yes" or "no" or "funny")) {
			return Single(false, trimmed, "Vote must be yes, no, or funny");
		}

		if (!TryParseReviewUrl(trimmed, out string profilePart, out uint appId)) {
			return Single(false, trimmed, "Invalid review URL");
		}

		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			if (!bot.IsConnectedAndLoggedOn) {
				return Single(false, trimmed, "Bot is not connected");
			}

			(ulong steamId, string? resolveError) = await ResolveProfileAsync(bot, profilePart).ConfigureAwait(false);

			if (steamId == 0) {
				return Single(false, trimmed, resolveError ?? "Could not resolve profile");
			}

			string? reviewId = await ResolveReviewIdAsync(bot, steamId, appId).ConfigureAwait(false);

			if (string.IsNullOrEmpty(reviewId)) {
				return Single(false, trimmed, "Could not find review id on page");
			}

			bool ok = voteKey switch {
				"yes" => await RateReviewAsync(bot, reviewId, rateUp: true).ConfigureAwait(false),
				"no" => await RateReviewAsync(bot, reviewId, rateUp: false).ConfigureAwait(false),
				"funny" => await VoteFunnyAsync(bot, reviewId).ConfigureAwait(false),
				_ => false,
			};

			return ok
				? Single(true, reviewId, "OK — " + voteKey)
				: Single(false, reviewId, "Vote failed (Steam rejected the request)");
		} catch (Exception e) {
			return Single(false, trimmed, e.Message);
		}
	}

	internal static bool TryParseReviewUrl(string value, out string profilePart, out uint appId) {
		profilePart = "";
		appId = 0;
		Match match = ReviewUrlRegex.Match(value.Trim());

		if (!match.Success) {
			return false;
		}

		profilePart = Uri.UnescapeDataString(match.Groups[1].Value);
		return uint.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId) && appId > 0;
	}

	private static async Task<(ulong SteamId, string? Error)> ResolveProfileAsync(Bot bot, string profilePart) {
		if (ulong.TryParse(profilePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong steamId) && steamId > 0) {
			SteamID sid = new(steamId);

			if (sid.IsIndividualAccount) {
				return (steamId, null);
			}
		}

		return await SteamIdResolver.ResolveAsync(bot, "https://steamcommunity.com/id/" + profilePart + "/").ConfigureAwait(false);
	}

	private static async Task<string?> ResolveReviewIdAsync(Bot bot, ulong steamId, uint appId) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/profiles/{steamId}/recommended/{appId}?l=english");
		BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

		if (response?.Content == null || response.Content.Count == 0) {
			return null;
		}

		string html = Encoding.UTF8.GetString([.. response.Content]);
		Match match = ReviewIdRegex.Match(html);

		return match.Success ? match.Groups[1].Value : null;
	}

	private static async Task<bool> RateReviewAsync(Bot bot, string reviewId, bool rateUp) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/userreviews/rate/{reviewId}");
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
			{ "rateup", rateUp ? "true" : "false" },
		};

		ObjectResponse<ResultResponse>? response = await bot.ArchiWebHandler
			.UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data)
			.ConfigureAwait(false);

		return response?.Content?.Result == EResult.OK;
	}

	private static async Task<bool> VoteFunnyAsync(Bot bot, string reviewId) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/userreviews/votetag/{reviewId}");
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "tagid", "1" },
			{ "rateup", "true" },
		};

		ObjectResponse<ResultResponse>? response = await bot.ArchiWebHandler
			.UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data)
			.ConfigureAwait(false);

		return response?.Content?.Result == EResult.OK;
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
