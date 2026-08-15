using System;
using System.Collections.Concurrent;
using System.Net;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ASFBotSocial.Services;

/// <summary>
/// Per-bot / per-endpoint cooldown for IPC reads.
/// Complements frontend cache against spam / bypassed UI.
/// </summary>
internal sealed class EndpointRateLimiter {
	private readonly ConcurrentDictionary<string, DateTime> lastUtc = new(StringComparer.OrdinalIgnoreCase);
	private readonly TimeSpan cooldown;

	public EndpointRateLimiter(TimeSpan cooldown) {
		this.cooldown = cooldown;
	}

	public ActionResult<GenericResponse>? TryAcquire(string botName, string endpoint) {
		string key = botName + ":" + endpoint;
		DateTime now = DateTime.UtcNow;

		if (lastUtc.TryGetValue(key, out DateTime last)) {
			TimeSpan wait = cooldown - (now - last);

			if (wait > TimeSpan.Zero) {
				return new ObjectResult(
					new GenericResponse(false, $"Rate limited. Retry in {(int) Math.Ceiling(wait.TotalSeconds)}s")
				) {
					StatusCode = (int) HttpStatusCode.TooManyRequests,
				};
			}
		}

		lastUtc[key] = now;

		return null;
	}
}
