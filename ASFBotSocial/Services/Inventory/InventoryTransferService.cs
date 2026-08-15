using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ASFBotSocial.Models;

namespace ASFBotSocial.Services;

internal sealed class InventoryTransferService {
	private static readonly RateLimiter TransferLimiter = new(TimeSpan.FromSeconds(10));

	public async Task<TransferResponse> TransferToBotAsync(Bot sourceBot, TransferRequest request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(sourceBot);
		ArgumentNullException.ThrowIfNull(request);

		if (!sourceBot.IsConnectedAndLoggedOn) {
			return Fail("Source bot is not connected");
		}

		if (string.IsNullOrWhiteSpace(request.TargetBotName)) {
			return Fail("TargetBotName required");
		}

		if ((request.AssetIds == null) || (request.AssetIds.Count == 0)) {
			return Fail("AssetIds required");
		}

		Bot? targetBot = Bot.GetBot(request.TargetBotName.Trim());

		if (targetBot == null) {
			return Fail($"Target bot not found: {request.TargetBotName}");
		}

		if (!targetBot.IsConnectedAndLoggedOn) {
			return Fail($"Target bot is not connected: {targetBot.BotName}");
		}

		if (targetBot.SteamID == sourceBot.SteamID) {
			return Fail("Cannot transfer to the same Steam account");
		}

		uint appId = request.AppId is > 0 ? request.AppId.Value : Asset.SteamAppID;
		ulong contextId = request.ContextId is > 0 ? request.ContextId.Value : Asset.SteamCommunityContextID;

		HashSet<ulong> wanted = [];
		List<TransferSkip> skipped = [];

		foreach (string raw in request.AssetIds) {
			if (!ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong assetId) || (assetId == 0)) {
				skipped.Add(new TransferSkip { AssetId = raw ?? "", Reason = "InvalidAssetId" });
				continue;
			}

			wanted.Add(assetId);
		}

		if (wanted.Count == 0) {
			return new TransferResponse {
				Ok = false,
				Requested = request.AssetIds.Count,
				Transferred = 0,
				Message = "No valid AssetIds",
				Skipped = skipped,
			};
		}

		await TransferLimiter.WaitAsync(sourceBot.BotName, cancellationToken).ConfigureAwait(false);

		HashSet<Asset> toSend;

		try {
			HashSet<ulong> found = [];
			toSend = await sourceBot.ArchiHandler
				.GetMyInventoryAsync(appId, contextId, tradableOnly: true)
				.Where(item => {
					if (!wanted.Contains(item.AssetID)) {
						return false;
					}

					found.Add(item.AssetID);
					return true;
				})
				.ToHashSetAsync()
				.ConfigureAwait(false);

			foreach (ulong assetId in wanted.Where(id => !found.Contains(id))) {
				skipped.Add(new TransferSkip {
					AssetId = assetId.ToString(CultureInfo.InvariantCulture),
					Reason = "MissingOrNotTradable",
				});
			}
		} catch (Exception e) {
			sourceBot.ArchiLogger.LogGenericWarningException(e);

			return Fail($"Failed to load inventory: {e.Message}", request.AssetIds.Count, skipped);
		}

		if (toSend.Count == 0) {
			return new TransferResponse {
				Ok = false,
				Requested = wanted.Count,
				Transferred = 0,
				Message = "No matching tradable items found in inventory",
				TargetBotName = targetBot.BotName,
				TargetSteamId = targetBot.SteamID.ToString(CultureInfo.InvariantCulture),
				Skipped = skipped,
			};
		}

		string? message = string.IsNullOrWhiteSpace(request.Message)
			? $"ASFBotSocial → {targetBot.BotName}"
			: request.Message.Trim();

		if (message.Length > 128) {
			message = message[..128];
		}

		(bool success, string asfMessage) = await sourceBot.Actions
			.SendInventory(toSend, targetBot.SteamID, customMessage: message)
			.ConfigureAwait(false);

		return new TransferResponse {
			Ok = success,
			Requested = wanted.Count,
			Transferred = success ? toSend.Count : 0,
			Message = asfMessage,
			TargetBotName = targetBot.BotName,
			TargetSteamId = targetBot.SteamID.ToString(CultureInfo.InvariantCulture),
			Skipped = skipped,
		};
	}

	private static TransferResponse Fail(string message, int requested = 0, IReadOnlyList<TransferSkip>? skipped = null) => new() {
		Ok = false,
		Requested = requested,
		Transferred = 0,
		Message = message,
		Skipped = skipped ?? Array.Empty<TransferSkip>(),
	};
}
