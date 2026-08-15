using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
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

internal sealed class CuratorsService {
	private static readonly Regex CuratorIdRegex = new(
		@"store\.steampowered\.com/curator/(\d+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(2));

	public async Task<MutationsResponse> FollowAsync(Bot bot, IReadOnlyCollection<string> targets, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(targets);

		List<MutationResult> results = [];

		foreach (string target in targets) {
			string trimmed = (target ?? "").Trim();

			if (string.IsNullOrEmpty(trimmed)) {
				results.Add(new MutationResult { Success = false, Target = "", Message = "Empty target" });

				continue;
			}

			await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			try {
				if (!bot.IsConnectedAndLoggedOn) {
					results.Add(new MutationResult { Success = false, Target = trimmed, Message = "Bot is not connected" });

					continue;
				}

				if (!TryParseCuratorId(trimmed, out ulong clanId)) {
					results.Add(new MutationResult { Success = false, Target = trimmed, Message = "Invalid curator URL or ID" });

					continue;
				}

				string targetLabel = clanId.ToString(CultureInfo.InvariantCulture);
				bool ok = await FollowCuratorAsync(bot, clanId).ConfigureAwait(false);

				results.Add(
					ok
						? new MutationResult { Success = true, Target = targetLabel, Message = "OK" }
						: new MutationResult {
							Success = false,
							Target = targetLabel,
							Message = "FollowCurator failed (limited account or Steam rejected the request)",
						}
				);
			} catch (Exception e) {
				results.Add(new MutationResult { Success = false, Target = trimmed, Message = e.Message });
			}
		}

		return new MutationsResponse { Results = results };
	}

	internal static bool TryParseCuratorId(string value, out ulong clanId) {
		clanId = 0;
		string trimmed = value.Trim().TrimEnd('/');

		Match urlMatch = CuratorIdRegex.Match(trimmed);

		if (urlMatch.Success) {
			return ulong.TryParse(urlMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out clanId) && clanId > 0;
		}

		if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong direct) && direct > 0) {
			clanId = direct;

			return true;
		}

		return false;
	}

	private static async Task<bool> FollowCuratorAsync(Bot bot, ulong clanId) {
		Uri request = new(ArchiWebHandler.SteamStoreURL, "/curators/ajaxfollow");
		Uri referer = new(ArchiWebHandler.SteamStoreURL, $"/curator/{clanId}");
		Dictionary<string, string> data = new(3, StringComparer.Ordinal) {
			{ "clanid", clanId.ToString(CultureInfo.InvariantCulture) },
			{ "follow", "1" },
		};

		// Steam returns { "success": { "success": 1 } } for curator follow.
		ObjectResponse<CuratorFollowAjaxResponse>? response = await bot.ArchiWebHandler
			.UrlPostToJsonObjectWithSession<CuratorFollowAjaxResponse>(request, data: data, referer: referer)
			.ConfigureAwait(false);

		if (response?.Content == null) {
			return false;
		}

		ResultResponse? nested = response.Content.Success;

		return nested == null || nested.Result == EResult.OK;
	}

	private sealed class CuratorFollowAjaxResponse {
		[JsonInclude]
		[JsonPropertyName("success")]
		public ResultResponse? Success { get; private init; }
	}
}
