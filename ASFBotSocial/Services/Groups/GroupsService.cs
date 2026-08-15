using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ASFBotSocial.Models;

namespace ASFBotSocial.Services;

internal sealed class GroupsService {
	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(2));

	public async Task<MutationsResponse> JoinAsync(Bot bot, IReadOnlyCollection<string> targets, CancellationToken cancellationToken = default) {
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

				(ulong clanId, string? name, string? error) = await SteamClanResolver.ResolveAsync(bot, trimmed).ConfigureAwait(false);

				if (clanId == 0) {
					results.Add(new MutationResult { Success = false, Target = trimmed, Message = error ?? "Resolve failed" });

					continue;
				}

				string targetLabel = clanId.ToString(CultureInfo.InvariantCulture);
				bool joined = await bot.ArchiWebHandler.JoinGroup(clanId).ConfigureAwait(false);

				if (joined) {
					results.Add(
						new MutationResult {
							Success = true,
							Target = targetLabel,
							Message = string.IsNullOrEmpty(name) ? "OK" : "OK — " + name,
						}
					);
				} else {
					results.Add(
						new MutationResult {
							Success = false,
							Target = targetLabel,
							Message = "JoinGroup failed (group may be private, invite-only, or Steam rejected the request)",
						}
					);
				}
			} catch (Exception e) {
				results.Add(new MutationResult { Success = false, Target = trimmed, Message = e.Message });
			}
		}

		return new MutationsResponse { Results = results };
	}
}
