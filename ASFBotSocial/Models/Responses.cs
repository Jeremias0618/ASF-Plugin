using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ASFBotSocial.Models;

public sealed class PluginStatusResponse {
	[JsonInclude]
	[Description("Plugin name")]
	public string Plugin { get; init; } = "ASFBotSocial";

	[JsonInclude]
	[Description("Plugin version")]
	public string Version { get; init; } = "1.0.0";

	[JsonInclude]
	[Description("Supported capability ids")]
	public IReadOnlyList<string> Capabilities { get; init; } = [
		"friends.read",
		"friends.write",
		"points.read",
		"followers.read",
		"followers.write",
		"curators.write",
		"reviews.write",
		"sharedfiles.write",
		"games.read",
		"games.search",
		"games.add",
		"games.stats",
		"games.boosterIdle",
		"wishlist.read",
		"wishlist.write",
		"wishlist.follow",
		"discovery.read",
		"discovery.explore",
		"inventory.transfer",
		"trades.read",
		"trades.cancel",
	];
}

/// <summary>Steam loyalty / points shop balance for one bot.</summary>
public sealed class PointsBalanceResponse {
	[JsonInclude]
	public long? Points { get; init; }
}

/// <summary>Public follower count for the bot Steam profile.</summary>
public sealed class FollowersCountResponse {
	[JsonInclude]
	public int? Count { get; init; }
}

public sealed class TransferSkip {
	[JsonInclude]
	public string AssetId { get; init; } = "";

	[JsonInclude]
	public string Reason { get; init; } = "";
}

public sealed class TransferResponse {
	[JsonInclude]
	public bool Ok { get; init; }

	[JsonInclude]
	public int Requested { get; init; }

	[JsonInclude]
	public int Transferred { get; init; }

	[JsonInclude]
	public string Message { get; init; } = "";

	[JsonInclude]
	public string? TargetBotName { get; init; }

	[JsonInclude]
	public string? TargetSteamId { get; init; }

	[JsonInclude]
	public IReadOnlyList<TransferSkip> Skipped { get; init; } = [];
}

public sealed class FriendEntry {
	[JsonInclude]
	public string SteamId { get; init; } = "";

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public string Relationship { get; init; } = "";

	[JsonInclude]
	public string? AvatarHash { get; init; }

	[JsonInclude]
	public string? PersonaState { get; init; }
}

public sealed class FriendsResponse {
	[JsonInclude]
	public int Total { get; init; }

	[JsonInclude]
	public IReadOnlyList<FriendEntry> Friends { get; init; } = [];

	[JsonInclude]
	public IReadOnlyList<FriendEntry> SentRequests { get; init; } = [];

	[JsonInclude]
	public IReadOnlyList<FriendEntry> ReceivedRequests { get; init; } = [];
}

public sealed class GameEntry {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public bool IsOwned { get; init; } = true;

	[JsonInclude]
	public bool IsShared { get; init; }

	[JsonInclude]
	public bool HasAchievements { get; init; }

	[JsonInclude]
	public bool HasCards { get; init; }

	/// <summary>Normalized Steam app type: game, dlc, demo, application, tool, beta, video, music, other.</summary>
	[JsonInclude]
	public string AppType { get; init; } = "game";
}

public sealed class GamesResponse {
	[JsonInclude]
	public int Total { get; init; }

	[JsonInclude]
	public int OwnedTotal { get; init; }

	[JsonInclude]
	public int SharedTotal { get; init; }

	[JsonInclude]
	public IReadOnlyList<GameEntry> Games { get; init; } = [];
}

public sealed class GameSearchHit {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public string? TinyImage { get; set; }

	[JsonInclude]
	public string? Currency { get; init; }

	/// <summary>Price in Steam cents (initial).</summary>
	[JsonInclude]
	public int? InitialPrice { get; init; }

	/// <summary>Price in Steam cents (final).</summary>
	[JsonInclude]
	public int? FinalPrice { get; init; }

	[JsonInclude]
	public int? DiscountPercent { get; init; }

	[JsonInclude]
	public bool Owned { get; set; }

	/// <summary>True when this hit is itself a Steam demo app.</summary>
	[JsonInclude]
	public bool IsDemo { get; set; }

	/// <summary>First free demo AppID linked from the full game store page, if any.</summary>
	[JsonInclude]
	public uint? DemoAppId { get; set; }

	/// <summary>Whether <see cref="DemoAppId"/> is already in the bot library.</summary>
	[JsonInclude]
	public bool DemoOwned { get; set; }
}

public sealed class GameSearchResponse {
	[JsonInclude]
	public string Query { get; init; } = "";

	[JsonInclude]
	public int Total { get; init; }

	[JsonInclude]
	public IReadOnlyList<GameSearchHit> Items { get; init; } = [];
}

/// <summary>Resolved Steam store artwork (hashed CDN paths for newer apps/demos).</summary>
public sealed class GameCoverResponse {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string? HeaderImage { get; init; }

	[JsonInclude]
	public string? CapsuleImage { get; init; }
}

public sealed class GameStatsEntry {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public uint PlaytimeMinutes { get; init; }

	[JsonInclude]
	public uint LastPlayedUnix { get; init; }

	[JsonInclude]
	public string? HeaderImage { get; init; }

	[JsonInclude]
	public uint? AchievementsUnlocked { get; init; }

	[JsonInclude]
	public uint? AchievementsTotal { get; init; }

	[JsonInclude]
	public bool IsOwned { get; init; } = true;

	[JsonInclude]
	public bool IsShared { get; init; }

	[JsonInclude]
	public bool HasCards { get; init; }
}

public sealed class GameStatsResponse {
	[JsonInclude]
	public double TotalPlaytimeHours { get; init; }

	[JsonInclude]
	public int InCollection { get; init; }

	[JsonInclude]
	public int Played { get; init; }

	[JsonInclude]
	public int NeverPlayed { get; init; }

	[JsonInclude]
	public IReadOnlyList<GameStatsEntry> Games { get; init; } = [];
}

/// <summary>Games eligible for Steam booster packs, ranked for GamesPlayedWhileIdle.</summary>
public sealed class BoosterIdleSuggestionEntry {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public uint PlaytimeMinutes { get; init; }
}

public sealed class BoosterIdleSuggestionsResponse {
	[JsonInclude]
	public int EligibleTotal { get; init; }

	[JsonInclude]
	public int SelectedTotal { get; init; }

	[JsonInclude]
	public int MaxIdle { get; init; } = 32;

	/// <summary>Top MaxIdle games by playtime (initial idle list).</summary>
	[JsonInclude]
	public IReadOnlyList<BoosterIdleSuggestionEntry> Games { get; init; } = [];

	/// <summary>Full booster-eligible pool ranked by playtime (for random replacements).</summary>
	[JsonInclude]
	public IReadOnlyList<BoosterIdleSuggestionEntry> Pool { get; init; } = [];
}

public sealed class AchievementEntry {
	/// <summary>1-based index used by unlock/lock endpoints.</summary>
	[JsonInclude]
	public uint Index { get; init; }

	[JsonInclude]
	public string? ApiName { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public string Description { get; init; } = "";

	[JsonInclude]
	public string? IconUrl { get; init; }

	[JsonInclude]
	public bool Unlocked { get; init; }

	[JsonInclude]
	public bool Restricted { get; init; }

	[JsonInclude]
	public bool Unlockable { get; init; }
}

public sealed class GameAchievementsResponse {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public string? HeaderImage { get; init; }

	[JsonInclude]
	public uint Unlocked { get; init; }

	[JsonInclude]
	public uint Total { get; init; }

	[JsonInclude]
	public IReadOnlyList<AchievementEntry> Achievements { get; init; } = [];
}

public sealed class AchievementMutationResponse {
	[JsonInclude]
	public bool Success { get; init; }

	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public uint Changed { get; init; }

	[JsonInclude]
	public string Message { get; init; } = "";
}

public sealed class WishlistEntry {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public uint? Priority { get; init; }
}

public sealed class WishlistResponse {
	[JsonInclude]
	public int Total { get; init; }

	[JsonInclude]
	public IReadOnlyList<WishlistEntry> Items { get; init; } = [];
}

public sealed class MutationResult {
	[JsonInclude]
	public bool Success { get; init; }

	[JsonInclude]
	public string Target { get; init; } = "";

	[JsonInclude]
	public string Message { get; init; } = "";
}

public sealed class MutationsResponse {
	[JsonInclude]
	public IReadOnlyList<MutationResult> Results { get; init; } = [];
}

public sealed class TradeItemView {
	[JsonInclude]
	public string AssetId { get; init; } = "";

	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string ContextId { get; init; } = "";

	[JsonInclude]
	public uint Amount { get; init; }

	[JsonInclude]
	public string ClassId { get; init; } = "";

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public string Type { get; init; } = "";

	[JsonInclude]
	public string Game { get; init; } = "";

	[JsonInclude]
	public string IconUrl { get; init; } = "";

	[JsonInclude]
	public string IconUrlLarge { get; init; } = "";

	[JsonInclude]
	public string BackgroundColor { get; init; } = "";
}

public sealed class TradeOfferView {
	[JsonInclude]
	public string TradeOfferId { get; init; } = "";

	[JsonInclude]
	public string State { get; init; } = "";

	/// <summary>sent | received</summary>
	[JsonInclude]
	public string Direction { get; init; } = "";

	/// <summary>needs_confirmation | waiting_partner | waiting_bot</summary>
	[JsonInclude]
	public string WaitingFor { get; init; } = "";

	[JsonInclude]
	public string PartnerSteamId { get; init; } = "";

	[JsonInclude]
	public string PartnerName { get; init; } = "";

	[JsonInclude]
	public string? PartnerAvatarHash { get; init; }

	[JsonInclude]
	public IReadOnlyList<TradeItemView> ItemsToGive { get; init; } = [];

	[JsonInclude]
	public IReadOnlyList<TradeItemView> ItemsToReceive { get; init; } = [];
}

public sealed class PendingTradeOffersResponse {
	[JsonInclude]
	public int Total { get; init; }

	[JsonInclude]
	public IReadOnlyList<TradeOfferView> Offers { get; init; } = [];
}

public sealed class TradeOfferActionResponse {
	[JsonInclude]
	public bool Ok { get; init; }

	[JsonInclude]
	public string TradeOfferId { get; init; } = "";

	[JsonInclude]
	public string Action { get; init; } = "";

	[JsonInclude]
	public string Message { get; init; } = "";
}

public sealed class DiscoveryQueueStatusResponse {
	[JsonInclude]
	public bool Available { get; init; }

	[JsonInclude]
	public bool CompletedToday { get; init; }

	[JsonInclude]
	public string? Detail { get; init; }
}

public sealed class DiscoveryQueueExploreResponse {
	[JsonInclude]
	public bool Success { get; init; }

	[JsonInclude]
	public byte QueuesCompleted { get; init; }

	[JsonInclude]
	public int AppsCleared { get; init; }

	[JsonInclude]
	public string Message { get; init; } = "";
}
