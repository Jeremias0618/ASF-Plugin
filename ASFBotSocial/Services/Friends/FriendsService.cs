using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ASFBotSocial.Models;
using SteamKit2;
using SteamKit2.Internal;

namespace ASFBotSocial.Services;

internal sealed class FriendsService {
	private readonly RateLimiter rateLimiter = new();

	public async Task<FriendsResponse> ListAsync(Bot bot, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		int count = bot.SteamFriends.GetFriendCount();
		List<FriendEntry> friends = new(Math.Max(0, count));
		List<(ulong SteamId, EFriendRelationship Relationship)> requestQueue = [];

		for (int i = 0; i < count; i++) {
			ulong steamId = bot.SteamFriends.GetFriendByIndex(i);
			EFriendRelationship relationship = bot.SteamFriends.GetFriendRelationship(steamId);

			switch (relationship) {
				case EFriendRelationship.Friend:
				case EFriendRelationship.Blocked:
					friends.Add(MapEntry(bot, steamId, relationship));
					break;
				case EFriendRelationship.RequestInitiator:
				case EFriendRelationship.RequestRecipient:
					bot.SteamFriends.RequestFriendInfo(
						steamId,
						EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence
					);
					requestQueue.Add((steamId, relationship));
					break;
			}
		}

		// Give Steam a brief moment to fill persona names for pending requests.
		if (requestQueue.Count > 0) {
			try {
				await Task.Delay(700, cancellationToken).ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Keep building with whatever names we already have.
			}
		}

		List<FriendEntry> sent = new(requestQueue.Count);
		List<FriendEntry> received = new(requestQueue.Count);

		foreach ((ulong steamId, EFriendRelationship relationship) in requestQueue) {
			FriendEntry entry = MapEntry(bot, steamId, relationship);
			if (relationship == EFriendRelationship.RequestInitiator) {
				sent.Add(entry);
			} else {
				received.Add(entry);
			}
		}

		friends.Sort(CompareEntries);
		sent.Sort(CompareEntries);
		received.Sort(CompareEntries);

		return new FriendsResponse {
			Total = friends.Count,
			Friends = friends,
			SentRequests = sent,
			ReceivedRequests = received,
		};
	}

	public async Task<MutationsResponse> AddAsync(Bot bot, IReadOnlyCollection<string> targets, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(targets);

		List<MutationResult> results = [];

		foreach (string target in targets) {
			await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			try {
				(ulong steamId, string? error) = await SteamIdResolver.ResolveAsync(bot, target).ConfigureAwait(false);

				if (steamId == 0) {
					results.Add(new MutationResult { Success = false, Target = target, Message = error ?? "Resolve failed" });

					continue;
				}

				EFriendRelationship before = bot.SteamFriends.GetFriendRelationship(steamId);

				if (before is EFriendRelationship.Friend or EFriendRelationship.RequestInitiator) {
					results.Add(
						new MutationResult {
							Success = true,
							Target = steamId.ToString(),
							Message = before == EFriendRelationship.Friend ? "Already friends" : "Request already pending",
						}
					);

					continue;
				}

				(bool ok, string message) = await AddFriendDetailedAsync(bot, steamId).ConfigureAwait(false);
				results.Add(
					new MutationResult {
						Success = ok,
						Target = steamId.ToString(),
						Message = message,
					}
				);
			} catch (Exception e) {
				results.Add(new MutationResult { Success = false, Target = target, Message = e.Message });
			}
		}

		return new MutationsResponse { Results = results };
	}

	public async Task<MutationsResponse> RemoveAsync(Bot bot, IReadOnlyCollection<ulong> steamIds, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(steamIds);

		List<MutationResult> results = [];

		foreach (ulong steamId in steamIds) {
			await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			if ((steamId == 0) || !new SteamID(steamId).IsIndividualAccount) {
				results.Add(new MutationResult { Success = false, Target = steamId.ToString(), Message = "Invalid SteamID" });

				continue;
			}

			bool ok = await bot.ArchiHandler.RemoveFriend(steamId).ConfigureAwait(false);
			results.Add(
				new MutationResult {
					Success = ok,
					Target = steamId.ToString(),
					Message = ok ? "OK" : "RemoveFriend failed",
				}
			);
		}

		return new MutationsResponse { Results = results };
	}

	/// <summary>
	/// Uses Player.AddFriend and checks Body.invite_sent / friend_relationship.
	/// Header EResult.OK alone is not enough — Steam often returns OK with invite_sent=false.
	/// </summary>
	private static async Task<(bool Ok, string Message)> AddFriendDetailedAsync(Bot bot, ulong steamId) {
		SteamUnifiedMessages? unified = bot.GetHandler<SteamUnifiedMessages>();

		if (unified == null) {
			return (false, "SteamUnifiedMessages unavailable");
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return (false, "Bot is not connected");
		}

		Player player = unified.CreateService<Player>();
		CPlayer_AddFriend_Request request = new() {
			steamid = steamId,
		};

		SteamUnifiedMessages.ServiceMethodResponse<CPlayer_AddFriend_Response> response;

		try {
			response = await player.AddFriend(request).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			return (false, e.Message);
		}

		if (response.Result != EResult.OK) {
			return (false, $"Steam EResult: {response.Result}");
		}

		CPlayer_AddFriend_Response body = response.Body;
		bool inviteSent = body.invite_sent;
		EFriendRelationship bodyRelationship = (EFriendRelationship) body.friend_relationship;

		// Proto field "result" is often unset (0). Non-zero means an explicit Steam failure code.
		if (body.result != 0 && (EResult) body.result != EResult.OK) {
			return (false, $"Invite rejected: {(EResult) body.result}");
		}

		if (inviteSent || IsActiveFriendRelationship(bodyRelationship)) {
			return (true, bodyRelationship == EFriendRelationship.None ? "RequestInitiator" : bodyRelationship.ToString());
		}

		// Fallback: legacy ClientAddFriend + wait for FriendsList callback.
		bot.SteamFriends.AddFriend(new SteamID(steamId));

		for (int attempt = 0; attempt < 5; attempt++) {
			await Task.Delay(400).ConfigureAwait(false);
			EFriendRelationship rel = bot.SteamFriends.GetFriendRelationship(steamId);

			if (IsActiveFriendRelationship(rel)) {
				return (true, rel.ToString());
			}
		}

		EFriendRelationship finalRel = bot.SteamFriends.GetFriendRelationship(steamId);

		return (
			false,
			$"Invite not sent (invite_sent={inviteSent.ToString(CultureInfo.InvariantCulture)}, body_rel={bodyRelationship}, cache_rel={finalRel}, body_result={body.result.ToString(CultureInfo.InvariantCulture)})"
		);
	}

	private static bool IsActiveFriendRelationship(EFriendRelationship relationship) =>
		relationship is EFriendRelationship.Friend or EFriendRelationship.RequestInitiator or EFriendRelationship.RequestRecipient;

	private static FriendEntry MapEntry(Bot bot, ulong steamId, EFriendRelationship relationship) {
		string? personaName = bot.SteamFriends.GetFriendPersonaName(steamId);
		string name = string.IsNullOrWhiteSpace(personaName) ? steamId.ToString() : personaName.Trim();
		EPersonaState personaState = bot.SteamFriends.GetFriendPersonaState(steamId);

		return new FriendEntry {
			SteamId = steamId.ToString(),
			Name = name,
			Relationship = relationship.ToString(),
			AvatarHash = ResolveAvatarHash(bot, steamId),
			PersonaState = personaState.ToString(),
		};
	}

	private static int CompareEntries(FriendEntry a, FriendEntry b) {
		int rel = RelationshipSortKey(a.Relationship).CompareTo(RelationshipSortKey(b.Relationship));
		if (rel != 0) {
			return rel;
		}

		return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
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

	private static int RelationshipSortKey(string relationship) => relationship switch {
		nameof(EFriendRelationship.Friend) => 0,
		nameof(EFriendRelationship.RequestRecipient) => 1,
		nameof(EFriendRelationship.RequestInitiator) => 2,
		nameof(EFriendRelationship.Blocked) => 3,
		_ => 4,
	};
}
