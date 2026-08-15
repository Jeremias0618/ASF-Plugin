using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using ASFBotSocial.Models;

namespace ASFBotSocial.Services;

internal sealed class DiscoveryQueueService {
	private const byte MaxQueuesPerRun = 3;

	private static readonly Regex SubtextRegex = new(
		@"<div[^>]*class=[""'][^""']*subtext[^""']*[""'][^>]*>([\s\S]*?)</div>",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex ExploreCueRegex = new(
		@"(You can get [\s\S]{0,160}?cards?|Come back tomorrow[\s\S]{0,80}|Start another queue|Start your queue|Click here to begin exploring)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(1.5));

	public async Task<DiscoveryQueueStatusResponse> GetStatusAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!bot.IsConnectedAndLoggedOn) {
			return new DiscoveryQueueStatusResponse {
				Available = false,
				CompletedToday = false,
				Detail = "Bot is not connected",
			};
		}

		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			// Prefer binary fetch so the plugin does not bind AngleSharp (not always loadable from plugins/).
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/explore?l=english");
			BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

			if (response?.Content == null || response.Content.Count == 0) {
				return new DiscoveryQueueStatusResponse {
					Available = true,
					CompletedToday = false,
					Detail = "Could not load Steam explore page — you can still explore.",
				};
			}

			string html = Encoding.UTF8.GetString([.. response.Content]);
			string detail = ExtractExploreDetail(html);
			string lower = detail.ToLowerInvariant();

			bool completedToday = lower.Contains("come back tomorrow", StringComparison.Ordinal)
				|| lower.Contains("already explored", StringComparison.Ordinal)
				|| lower.Contains("you've completed", StringComparison.Ordinal)
				|| lower.Contains("you have completed", StringComparison.Ordinal);

			bool available = detail.StartsWith("You can get ", StringComparison.Ordinal)
				|| lower.Contains("start another queue", StringComparison.Ordinal)
				|| lower.Contains("start your queue", StringComparison.Ordinal)
				|| lower.Contains("click here to begin", StringComparison.Ordinal)
				|| lower.Contains("begin exploring", StringComparison.Ordinal);

			if (!available && !completedToday && string.IsNullOrWhiteSpace(detail)) {
				available = true;
				detail = "Queue status unclear — you can still explore.";
			} else if (!available && !completedToday) {
				available = true;
			}

			return new DiscoveryQueueStatusResponse {
				Available = available && !completedToday,
				CompletedToday = completedToday,
				Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
			};
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return new DiscoveryQueueStatusResponse {
				Available = true,
				CompletedToday = false,
				Detail = string.IsNullOrWhiteSpace(e.Message)
					? $"{e.GetType().Name} — you can still explore."
					: e.Message,
			};
		}
	}

	public async Task<DiscoveryQueueExploreResponse> ExploreAsync(Bot bot, byte queues = 1, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!bot.IsConnectedAndLoggedOn) {
			return new DiscoveryQueueExploreResponse {
				Success = false,
				Message = "Bot is not connected",
			};
		}

		byte targetQueues = queues == 0 ? (byte) 1 : Math.Min(queues, MaxQueuesPerRun);
		byte queuesCompleted = 0;
		int appsCleared = 0;

		for (byte i = 0; i < targetQueues; i++) {
			cancellationToken.ThrowIfCancellationRequested();

			await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			ImmutableHashSet<uint>? queue;

			try {
				queue = await GenerateNewQueueAsync(bot).ConfigureAwait(false);
			} catch (Exception e) {
				return new DiscoveryQueueExploreResponse {
					Success = queuesCompleted > 0,
					QueuesCompleted = queuesCompleted,
					AppsCleared = appsCleared,
					Message = queuesCompleted > 0
						? $"Stopped after {queuesCompleted} queue(s): {e.Message}"
						: e.Message,
				};
			}

			if ((queue == null) || (queue.Count == 0)) {
				return new DiscoveryQueueExploreResponse {
					Success = queuesCompleted > 0,
					QueuesCompleted = queuesCompleted,
					AppsCleared = appsCleared,
					Message = queuesCompleted > 0
						? $"Completed {queuesCompleted} queue(s); next queue was empty"
						: "Steam returned an empty discovery queue",
				};
			}

			foreach (uint appId in queue) {
				cancellationToken.ThrowIfCancellationRequested();
				await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

				bool cleared = await ClearFromQueueAsync(bot, appId).ConfigureAwait(false);

				if (!cleared) {
					return new DiscoveryQueueExploreResponse {
						Success = false,
						QueuesCompleted = queuesCompleted,
						AppsCleared = appsCleared,
						Message = $"Failed clearing AppID {appId.ToString(CultureInfo.InvariantCulture)}",
					};
				}

				appsCleared++;
			}

			queuesCompleted++;
		}

		return new DiscoveryQueueExploreResponse {
			Success = true,
			QueuesCompleted = queuesCompleted,
			AppsCleared = appsCleared,
			Message = $"Cleared {appsCleared} app(s) across {queuesCompleted} queue(s)",
		};
	}

	private static string ExtractExploreDetail(string html) {
		Match match = SubtextRegex.Match(html);

		if (match.Success) {
			string text = StripHtml(match.Groups[1].Value);

			if (!string.IsNullOrWhiteSpace(text)) {
				return text;
			}
		}

		Match fallback = ExploreCueRegex.Match(html);

		return fallback.Success ? StripHtml(fallback.Groups[1].Value) : "";
	}

	private static string StripHtml(string value) {
		if (string.IsNullOrEmpty(value)) {
			return "";
		}

		char[] buffer = new char[value.Length];
		int n = 0;
		bool inTag = false;

		foreach (char c in value) {
			if (c == '<') {
				inTag = true;

				continue;
			}

			if (c == '>') {
				inTag = false;

				continue;
			}

			if (inTag) {
				continue;
			}

			buffer[n++] = c == '\u00a0' ? ' ' : c;
		}

		string text = new(buffer, 0, n);
		text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&#39;", "'");

		while (text.Contains("  ")) {
			text = text.Replace("  ", " ");
		}

		return text.Trim();
	}

	private static async Task<ImmutableHashSet<uint>?> GenerateNewQueueAsync(Bot bot) {
		Uri request = new(ArchiWebHandler.SteamStoreURL, "/explore/generatenewdiscoveryqueue");
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
			["queuetype"] = "0",
		};

		ObjectResponse<NewDiscoveryQueuePayload>? response = await bot.ArchiWebHandler
			.UrlPostToJsonObjectWithSession<NewDiscoveryQueuePayload>(request, data: data)
			.ConfigureAwait(false);

		IReadOnlyList<uint>? queue = response?.Content?.Queue;

		if ((queue == null) || (queue.Count == 0)) {
			return null;
		}

		return queue.ToImmutableHashSet();
	}

	private static async Task<bool> ClearFromQueueAsync(Bot bot, uint appId) {
		if (appId == 0) {
			return false;
		}

		Uri request = new(ArchiWebHandler.SteamStoreURL, $"/app/{appId.ToString(CultureInfo.InvariantCulture)}");
		Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
			["appid_to_clear_from_queue"] = appId.ToString(CultureInfo.InvariantCulture),
		};

		return await bot.ArchiWebHandler.UrlPostWithSession(request, data: data).ConfigureAwait(false);
	}

	private sealed class NewDiscoveryQueuePayload {
		[JsonInclude]
		[JsonPropertyName("queue")]
		public List<uint>? Queue { get; init; }
	}
}
