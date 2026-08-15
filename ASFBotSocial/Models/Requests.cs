using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ASFBotSocial.Models;

public sealed class AddFriendsRequest {
	[JsonInclude]
	[Required]
	public List<string> Targets { get; init; } = [];
}

public sealed class RemoveFriendsRequest {
	[JsonInclude]
	[Required]
	public List<ulong> SteamIds { get; init; } = [];
}

public sealed class WishlistMutationRequest {
	[JsonInclude]
	[Required]
	public List<uint> AppIds { get; init; } = [];
}

public sealed class AddGamesRequest {
	[JsonInclude]
	[Required]
	public List<uint> AppIds { get; init; } = [];
}

/// <summary>Unlock or lock achievements by 1-based index (or all unlockable).</summary>
public sealed class SetAchievementsRequest {
	/// <summary>1-based indices from Games/{appId}/Achievements.</summary>
	[JsonInclude]
	public List<uint>? Indices { get; init; }

	/// <summary>When true, apply to every non-restricted achievement.</summary>
	[JsonInclude]
	public bool All { get; init; }
}

/// <summary>P0: transfer selected Steam inventory assets to another ASF bot in the farm.</summary>
public sealed class TransferRequest {
	[JsonInclude]
	public uint? AppId { get; init; }

	[JsonInclude]
	public ulong? ContextId { get; init; }

	[JsonInclude]
	[Required]
	public List<string> AssetIds { get; init; } = [];

	[JsonInclude]
	[Required]
	public string TargetBotName { get; init; } = "";

	[JsonInclude]
	public string? Message { get; init; }
}

public sealed class CancelTradeOfferRequest {
	[JsonInclude]
	[Required]
	public string TradeOfferId { get; init; } = "";

	/// <summary>sent | received — determines Cancel vs Decline.</summary>
	[JsonInclude]
	[Required]
	public string Direction { get; init; } = "";
}

public sealed class JoinGroupsRequest {
	[JsonInclude]
	[Required]
	public List<string> Targets { get; init; } = [];
}

public sealed class FollowUsersRequest {
	[JsonInclude]
	[Required]
	public List<string> Targets { get; init; } = [];
}

public sealed class FollowCuratorsRequest {
	[JsonInclude]
	[Required]
	public List<string> Targets { get; init; } = [];
}

public sealed class VoteReviewRequest {
	[JsonInclude]
	[Required]
	public string Url { get; init; } = "";

	/// <summary>yes | no | funny</summary>
	[JsonInclude]
	[Required]
	public string Vote { get; init; } = "";
}

public sealed class SharedFileActionRequest {
	[JsonInclude]
	[Required]
	public string Url { get; init; } = "";

	/// <summary>like | dislike | empty when only favoriting</summary>
	[JsonInclude]
	public string? Vote { get; init; }

	[JsonInclude]
	public bool Favorite { get; init; }
}

/// <summary>Store URL or AppID — add to wishlist and follow game (skips actions already done).</summary>
public sealed class WishlistFollowRequest {
	[JsonInclude]
	[Required]
	public string Url { get; init; } = "";
}

/// <summary>How many discovery queues to generate and clear (Steam sale cards allow up to 3/day).</summary>
public sealed class DiscoveryQueueExploreRequest {
	[JsonInclude]
	public byte Queues { get; init; } = 1;
}
