using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace ASFBotSocial.Services;

/// <summary>
/// SteamKit handler for ClientGetUserStats / ClientStoreUserStats2 (same protocol path as SAM / ASFAchievementManager).
/// </summary>
internal sealed class AchievementHandler : ClientMsgHandler {
	private static readonly ConcurrentDictionary<Bot, AchievementHandler> Handlers = new();

	internal static void Register(Bot bot, AchievementHandler handler) => Handlers[bot] = handler;

	internal static AchievementHandler? For(Bot bot) => Handlers.TryGetValue(bot, out AchievementHandler? handler) ? handler : null;

	public override void HandleMsg(IPacketMsg packetMsg) {
		ArgumentNullException.ThrowIfNull(packetMsg);

		switch (packetMsg.MsgType) {
			case EMsg.ClientGetUserStatsResponse: {
				ClientMsgProtobuf<CMsgClientGetUserStatsResponse> message = new(packetMsg);
				Client.PostCallback(new GetUserStatsCallback(packetMsg.TargetJobID, message.Body));
				break;
			}
			case EMsg.ClientStoreUserStatsResponse: {
				ClientMsgProtobuf<CMsgClientStoreUserStatsResponse> message = new(packetMsg);
				Client.PostCallback(new StoreUserStatsCallback(packetMsg.TargetJobID, message.Body));
				break;
			}
		}
	}

	internal async Task<CMsgClientGetUserStatsResponse?> GetUserStatsAsync(Bot bot, uint appId) {
		if (!Client.IsConnected) {
			return null;
		}

		ClientMsgProtobuf<CMsgClientGetUserStats> request = new(EMsg.ClientGetUserStats) {
			SourceJobID = Client.GetNextJobID(),
			Body = {
				game_id = appId,
				steam_id_for_user = bot.SteamID,
			},
		};

		Client.Send(request);

		try {
			GetUserStatsCallback response = await new AsyncJob<GetUserStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);

			return response.Success ? response.Body : null;
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("GetUserStats failed: " + e.Message);

			return null;
		}
	}

	internal async Task<bool> StoreUserStatsAsync(Bot bot, uint appId, uint crcStats, IReadOnlyCollection<CMsgClientStoreUserStats2.Stats> stats) {
		if (!Client.IsConnected || (stats.Count == 0)) {
			return false;
		}

		ClientMsgProtobuf<CMsgClientStoreUserStats2> request = new(EMsg.ClientStoreUserStats2) {
			SourceJobID = Client.GetNextJobID(),
			Body = {
				game_id = appId,
				settor_steam_id = bot.SteamID,
				settee_steam_id = bot.SteamID,
				explicit_reset = false,
				crc_stats = crcStats,
			},
		};

		request.Body.stats.AddRange(stats);
		Client.Send(request);

		try {
			StoreUserStatsCallback response = await new AsyncJob<StoreUserStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);

			return response.Success;
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("StoreUserStats failed: " + e.Message);

			return false;
		}
	}

	internal sealed class AchievementStat {
		internal uint Index { get; init; }
		internal uint StatNum { get; init; }
		internal int BitNum { get; init; }
		internal bool IsSet { get; init; }
		internal bool Restricted { get; set; }
		internal uint StatValue { get; init; }
		internal uint Dependancy { get; set; }
		internal uint DependancyValue { get; init; }
		internal string? DependancyName { get; init; }
		internal string Name { get; init; } = "";
		internal string Description { get; init; } = "";
		internal string? ApiName { get; init; }
		internal string? Icon { get; init; }
		internal string? IconGray { get; init; }
	}

	internal static List<AchievementStat>? ParseAchievements(CMsgClientGetUserStatsResponse response) {
		if (response.schema == null) {
			return null;
		}

		KeyValue keyValues = new();

		using (MemoryStream stream = new(response.schema)) {
			if (!keyValues.TryReadAsBinary(stream)) {
				return null;
			}
		}

		List<AchievementStat> result = [];
		uint index = 0;

		foreach (KeyValue stat in keyValues.Children.Find(static child => child.Name == "stats")?.Children ?? []) {
			string? type = stat.Children.Find(static child => child.Name == "type")?.Value?.ToUpperInvariant();

			if ((type != "4") && (type != "ACHIEVEMENTS")) {
				continue;
			}

			if (!uint.TryParse(stat.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint statNum)) {
				continue;
			}

			uint? statValue = response.stats.Find(item => item.stat_id == statNum)?.stat_value;

			foreach (KeyValue achievement in stat.Children.Find(static child => child.Name == "bits")?.Children ?? []) {
				if (!int.TryParse(achievement.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bitNum)) {
					continue;
				}

				bool isSet = (statValue != null) && ((statValue & ((uint) 1 << bitNum)) != 0);
				bool restricted = achievement.Children.Find(static child => (child.Name == "permission") && (child.Value != null)) != null;
				KeyValue? progress = achievement.Children.Find(static child => child.Name == "progress");
				string? dependancyName = progress?.Children.Find(static child => child.Name == "value")?.Children.Find(static child => child.Name == "operand1")?.Value;
				_ = uint.TryParse(progress?.Children.Find(static child => child.Name == "max_val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint dependancyValue);

				KeyValue? display = achievement.Children.Find(static child => child.Name == "display");
				string name = ReadLocalized(display?.Children.Find(static child => child.Name == "name")) ?? ("Achievement " + (index + 1).ToString(CultureInfo.InvariantCulture));
				string description = ReadLocalized(display?.Children.Find(static child => child.Name == "desc")) ?? "";
				string? icon = display?.Children.Find(static child => child.Name == "icon")?.Value;
				string? iconGray = display?.Children.Find(static child => child.Name == "icon_gray")?.Value
					?? display?.Children.Find(static child => child.Name == "icongray")?.Value;
				string? apiName = achievement.Children.Find(static child => child.Name == "name")?.Value;

				result.Add(
					new AchievementStat {
						Index = ++index,
						StatNum = statNum,
						BitNum = bitNum,
						IsSet = isSet,
						Restricted = restricted,
						StatValue = statValue ?? 0,
						DependancyValue = dependancyValue,
						DependancyName = dependancyName,
						Name = name,
						Description = description,
						ApiName = apiName,
						Icon = icon,
						IconGray = iconGray,
					}
				);
			}
		}

		foreach (KeyValue stat in keyValues.Children.Find(static child => child.Name == "stats")?.Children ?? []) {
			string? type = stat.Children.Find(static child => child.Name == "type")?.Value?.ToUpperInvariant();

			if ((type != "1") && (type != "INT")) {
				continue;
			}

			if (!uint.TryParse(stat.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint statNum)) {
				continue;
			}

			bool restricted = int.TryParse(stat.Children.Find(static child => child.Name == "permission")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int permission) && (permission > 1);
			string? name = stat.Children.Find(static child => child.Name == "name")?.Value;

			if (string.IsNullOrEmpty(name)) {
				continue;
			}

			AchievementStat? parent = result.Find(item => item.DependancyName == name);

			if (parent == null) {
				continue;
			}

			parent.Dependancy = statNum;

			if (restricted && !parent.Restricted) {
				parent.Restricted = true;
			}
		}

		return result;
	}

	internal static IEnumerable<CMsgClientStoreUserStats2.Stats> BuildStatsToSet(
		List<CMsgClientStoreUserStats2.Stats> buffer,
		AchievementStat achievement,
		bool unlock
	) {
		CMsgClientStoreUserStats2.Stats? current = buffer.Find(stat => stat.stat_id == achievement.StatNum);

		if (current == null) {
			current = new CMsgClientStoreUserStats2.Stats {
				stat_id = achievement.StatNum,
				stat_value = achievement.StatValue,
			};

			yield return current;
		}

		uint mask = (uint) 1 << achievement.BitNum;

		if (unlock) {
			current.stat_value |= mask;
		} else {
			current.stat_value &= ~mask;
		}

		if (string.IsNullOrEmpty(achievement.DependancyName) || (achievement.Dependancy == 0)) {
			yield break;
		}

		if (buffer.Find(stat => stat.stat_id == achievement.Dependancy) != null) {
			yield break;
		}

		yield return new CMsgClientStoreUserStats2.Stats {
			stat_id = achievement.Dependancy,
			stat_value = unlock ? achievement.DependancyValue : 0,
		};
	}

	private static string? ReadLocalized(KeyValue? node) {
		if (node == null) {
			return null;
		}

		string? spanish = node.Children.Find(static child => child.Name is "spanish" or "latam")?.Value;
		string? english = node.Children.Find(static child => child.Name == "english")?.Value;

		return !string.IsNullOrWhiteSpace(spanish) ? spanish : english ?? node.Children.FirstOrDefault()?.Value;
	}

	private sealed class GetUserStatsCallback : CallbackMsg {
		internal readonly CMsgClientGetUserStatsResponse Body;
		internal readonly bool Success;

		internal GetUserStatsCallback(JobID jobID, CMsgClientGetUserStatsResponse body) {
			JobID = jobID;
			Body = body;
			Success = (EResult) body.eresult == EResult.OK;
		}
	}

	private sealed class StoreUserStatsCallback : CallbackMsg {
		internal readonly bool Success;

		internal StoreUserStatsCallback(JobID jobID, CMsgClientStoreUserStatsResponse body) {
			JobID = jobID;
			Success = (EResult) body.eresult == EResult.OK;
		}
	}
}
