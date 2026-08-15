using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ASFBotSocial.Services;

internal sealed class RateLimiter {
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, DateTime> LastActionUtc = new(StringComparer.OrdinalIgnoreCase);

	private readonly TimeSpan delay;

	public RateLimiter(TimeSpan? delay = null) {
		this.delay = delay ?? TimeSpan.FromSeconds(3);
	}

	public async Task WaitAsync(string botName, CancellationToken cancellationToken = default) {
		SemaphoreSlim gate = Locks.GetOrAdd(botName, static _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try {
			if (LastActionUtc.TryGetValue(botName, out DateTime last)) {
				TimeSpan wait = delay - (DateTime.UtcNow - last);

				if (wait > TimeSpan.Zero) {
					await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
				}
			}

			LastActionUtc[botName] = DateTime.UtcNow;
		} finally {
			gate.Release();
		}
	}
}
