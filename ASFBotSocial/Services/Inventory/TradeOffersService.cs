using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ASFBotSocial.Models;
using SteamKit2;

namespace ASFBotSocial.Services;

internal sealed class TradeOffersService {
	private static readonly RateLimiter MutateLimiter = new(TimeSpan.FromSeconds(3));

	public async Task<PendingTradeOffersResponse> ListPendingAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		// activeOffers:null returns all states; ASF's active_only=true filter drops CreatedNeedsConfirmation.
		HashSet<TradeOffer>? received = await bot.ArchiWebHandler.GetTradeOffers(
			activeOffers: null,
			receivedOffers: true,
			sentOffers: false,
			withDescriptions: true
		).ConfigureAwait(false);

		cancellationToken.ThrowIfCancellationRequested();

		HashSet<TradeOffer>? sent = await bot.ArchiWebHandler.GetTradeOffers(
			activeOffers: null,
			receivedOffers: false,
			sentOffers: true,
			withDescriptions: true
		).ConfigureAwait(false);

		List<TradeOfferView> offers = [];

		if (received != null) {
			foreach (TradeOffer offer in received.Where(IsPending)) {
				offers.Add(Map(bot, offer, isOurOffer: false));
			}
		}

		if (sent != null) {
			foreach (TradeOffer offer in sent.Where(IsPending)) {
				offers.Add(Map(bot, offer, isOurOffer: true));
			}
		}

		offers.Sort(static (a, b) => {
			int wait = WaitingSortKey(a.WaitingFor).CompareTo(WaitingSortKey(b.WaitingFor));
			if (wait != 0) {
				return wait;
			}

			return string.Compare(a.PartnerName, b.PartnerName, StringComparison.OrdinalIgnoreCase);
		});

		return new PendingTradeOffersResponse {
			Total = offers.Count,
			Offers = offers,
		};
	}

	public async Task<TradeOfferActionResponse> CancelOrDeclineAsync(Bot bot, CancelTradeOfferRequest request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(request);

		if (!ulong.TryParse(request.TradeOfferId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong tradeOfferId) || (tradeOfferId == 0)) {
			return Fail(request.TradeOfferId, "cancel", "Invalid TradeOfferId");
		}

		bool isOurOffer = string.Equals(request.Direction, "sent", StringComparison.OrdinalIgnoreCase);
		bool isReceived = string.Equals(request.Direction, "received", StringComparison.OrdinalIgnoreCase);

		if (!isOurOffer && !isReceived) {
			return Fail(request.TradeOfferId, "cancel", "Direction must be sent or received");
		}

		await MutateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		string action = isOurOffer ? "cancel" : "decline";
		bool ok = isOurOffer
			? await bot.ArchiWebHandler.CancelTradeOffer(tradeOfferId).ConfigureAwait(false)
			: await bot.ArchiWebHandler.DeclineTradeOffer(tradeOfferId).ConfigureAwait(false);

		// Sent offers awaiting mobile confirm also leave a Steam Guard confirmation — reject it when possible.
		if (isOurOffer && bot.HasMobileAuthenticator) {
			try {
				await bot.Actions.HandleTwoFactorAuthenticationConfirmations(
					accept: false,
					acceptedType: EMobileConfirmationType.Trade,
					acceptedCreatorIDs: [tradeOfferId],
					waitIfNeeded: false
				).ConfigureAwait(false);
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		return new TradeOfferActionResponse {
			Ok = ok,
			TradeOfferId = tradeOfferId.ToString(CultureInfo.InvariantCulture),
			Action = action,
			Message = ok ? "OK" : $"{action} failed",
		};
	}

	private static TradeOfferActionResponse Fail(string tradeOfferId, string action, string message) => new() {
		Ok = false,
		TradeOfferId = tradeOfferId ?? "",
		Action = action,
		Message = message,
	};

	private static bool IsPending(TradeOffer offer) =>
		offer.State is ETradeOfferState.Active or ETradeOfferState.CreatedNeedsConfirmation;

	private static int WaitingSortKey(string waitingFor) => waitingFor switch {
		"needs_confirmation" => 0,
		"waiting_bot" => 1,
		"waiting_partner" => 2,
		_ => 3,
	};

	private static TradeOfferView Map(Bot bot, TradeOffer offer, bool isOurOffer) {
		ulong partnerId = offer.OtherSteamID64;
		string partnerName = bot.SteamFriends.GetFriendPersonaName(partnerId) ?? partnerId.ToString();
		string? avatarHash = ResolveAvatarHash(bot, partnerId);

		string waitingFor = offer.State == ETradeOfferState.CreatedNeedsConfirmation
			? "needs_confirmation"
			: isOurOffer
				? "waiting_partner"
				: "waiting_bot";

		IReadOnlyCollection<Asset> itemsToGive = isOurOffer ? offer.ItemsToGiveReadOnly : offer.ItemsToReceiveReadOnly;
		IReadOnlyCollection<Asset> itemsToReceive = isOurOffer ? offer.ItemsToReceiveReadOnly : offer.ItemsToGiveReadOnly;

		return new TradeOfferView {
			TradeOfferId = offer.TradeOfferID.ToString(),
			State = offer.State.ToString(),
			Direction = isOurOffer ? "sent" : "received",
			WaitingFor = waitingFor,
			PartnerSteamId = partnerId.ToString(),
			PartnerName = partnerName,
			PartnerAvatarHash = avatarHash,
			ItemsToGive = itemsToGive.Select(MapItem).ToList(),
			ItemsToReceive = itemsToReceive.Select(MapItem).ToList(),
		};
	}

	private static TradeItemView MapItem(Asset asset) {
		InventoryDescription? desc = asset.Description;

		return new TradeItemView {
			AssetId = asset.AssetID.ToString(),
			AppId = asset.AppID,
			ContextId = asset.ContextID.ToString(),
			Amount = asset.Amount,
			ClassId = asset.ClassID.ToString(),
			Name = desc?.Name ?? desc?.MarketName ?? asset.AssetID.ToString(),
			Type = desc?.TypeText ?? "",
			Game = ResolveGameName(desc),
			IconUrl = desc?.IconURL ?? "",
			IconUrlLarge = !string.IsNullOrEmpty(desc?.IconURLLarge) ? desc!.IconURLLarge : (desc?.IconURL ?? ""),
			BackgroundColor = desc?.BackgroundColor ?? "",
		};
	}

	private static string ResolveGameName(InventoryDescription? desc) {
		if (desc?.Tags == null) {
			return "";
		}

		foreach (Tag tag in desc.Tags) {
			if (string.Equals(tag.Identifier, "Game", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(tag.LocalizedValue)) {
				return tag.LocalizedValue;
			}
		}

		return "";
	}

	private static string? ResolveAvatarHash(Bot bot, ulong steamId) {
		try {
			byte[]? avatar = bot.SteamFriends.GetFriendAvatar(steamId);

			if ((avatar == null) || (avatar.Length == 0) || avatar.All(static b => b == 0)) {
				return null;
			}

			return Convert.ToHexStringLower(avatar);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}
	}
}
