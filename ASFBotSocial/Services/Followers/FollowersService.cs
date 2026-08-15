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

internal sealed class FollowersService {
	private static readonly Regex FollowersCountRegex = new(
		@"/followers/?[""'\s>][\s\S]{0,800}?profile_count_link_total[^>]*>\s*([\d\s.,]+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex FollowersCountFallbackRegex = new(
		@"profile_count_link_total[^>]*>\s*([\d\s.,]+)\s*</span>[\s\S]{0,200}?followers",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(2));

	public async Task<FollowersCountResponse> GetCountAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!bot.IsConnectedAndLoggedOn || bot.SteamID == 0) {
			return new FollowersCountResponse { Count = null };
		}

		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/profiles/{bot.SteamID}");
			BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

			if (response?.Content == null || response.Content.Count == 0) {
				return new FollowersCountResponse { Count = null };
			}

			string html = Encoding.UTF8.GetString([.. response.Content]);
			int? count = TryParseFollowersCount(html);

			return new FollowersCountResponse { Count = count };
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Followers count failed: " + e.Message);

			return new FollowersCountResponse { Count = null };
		}
	}

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

				(ulong steamId, string? error) = await SteamIdResolver.ResolveAsync(bot, trimmed).ConfigureAwait(false);

				if (steamId == 0) {
					results.Add(new MutationResult { Success = false, Target = trimmed, Message = error ?? "Resolve failed" });

					continue;
				}

				if (steamId == bot.SteamID) {
					results.Add(new MutationResult { Success = false, Target = steamId.ToString(CultureInfo.InvariantCulture), Message = "Cannot follow yourself" });

					continue;
				}

				string targetLabel = steamId.ToString(CultureInfo.InvariantCulture);
				(bool ok, string message) = await FollowUserDetailedAsync(bot, steamId).ConfigureAwait(false);

				results.Add(
					new MutationResult {
						Success = ok,
						Target = targetLabel,
						Message = message,
					}
				);
			} catch (Exception e) {
				results.Add(new MutationResult { Success = false, Target = trimmed, Message = e.Message });
			}
		}

		return new MutationsResponse { Results = results };
	}

	private static async Task<(bool Ok, string Message)> FollowUserDetailedAsync(Bot bot, ulong steamId) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/profiles/{steamId}/followuser/");
		Dictionary<string, string> data = new(1, StringComparer.Ordinal);

		ObjectResponse<ResultResponse>? response = await bot.ArchiWebHandler
			.UrlPostToJsonObjectWithSession<ResultResponse>(request, data: data)
			.ConfigureAwait(false);

		if (response?.Content == null) {
			return (false, "FollowUser failed (no response)");
		}

		EResult result = response.Content.Result;

		if (result == EResult.OK) {
			return (true, "OK");
		}

		// Steam treats a redundant follow as DuplicateRequest when already following.
		if (result == EResult.DuplicateRequest) {
			return (true, "Already following");
		}

		// Some clients return Fail / other codes; confirm via profile action button.
		if (await IsAlreadyFollowingAsync(bot, steamId).ConfigureAwait(false)) {
			return (true, "Already following");
		}

		return (false, $"FollowUser failed ({result})");
	}

	private static async Task<bool> IsAlreadyFollowingAsync(Bot bot, ulong steamId) {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, $"/profiles/{steamId}");

		BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

		if (response?.Content == null || response.Content.Count == 0) {
			return false;
		}

		string html = Encoding.UTF8.GetString([.. response.Content]);

		return html.Contains("unfollowuser", StringComparison.OrdinalIgnoreCase);
	}

	private static int? TryParseFollowersCount(string html) {
		Match match = FollowersCountRegex.Match(html);

		if (!match.Success) {
			match = FollowersCountFallbackRegex.Match(html);
		}

		if (!match.Success) {
			return null;
		}

		string raw = match.Groups[1].Value.Replace(" ", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);

		return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : null;
	}
}
