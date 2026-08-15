using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Steam;
using ASFBotSocial.Models;
using ASFBotSocial.Services;
using Microsoft.AspNetCore.Mvc;

namespace ASFBotSocial.Controllers;

[Route("/Api/BotSocial/{botNames:required}")]
public sealed class BotSocialController : ArchiController {
	private static readonly StringComparer BotNameComparer = StringComparer.OrdinalIgnoreCase;
	private static readonly FriendsService Friends = new();
	private static readonly GroupsService Groups = new();
	private static readonly FollowersService Followers = new();
	private static readonly CuratorsService Curators = new();
	private static readonly ReviewsService Reviews = new();
	private static readonly SharedFilesService SharedFiles = new();
	private static readonly GamesService Games = new();
	private static readonly AchievementsService Achievements = new();
	private static readonly WishlistService Wishlist = new();
	private static readonly DiscoveryQueueService DiscoveryQueue = new();
	private static readonly InventoryTransferService InventoryTransfer = new();
	private static readonly TradeOffersService TradeOffers = new();
	private static readonly EndpointRateLimiter FriendsReadLimiter = new(TimeSpan.FromSeconds(2));
	private static readonly EndpointRateLimiter FriendsAddLimiter = new(TimeSpan.FromSeconds(4));
	private static readonly EndpointRateLimiter FriendsRemoveLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter GroupsJoinLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter FollowersReadLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter FollowersWriteLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter CuratorsWriteLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter ReviewsWriteLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter SharedFilesWriteLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter GamesReadLimiter = new(TimeSpan.FromSeconds(2));
	private static readonly EndpointRateLimiter GamesSearchLimiter = new(TimeSpan.FromSeconds(1));
	private static readonly EndpointRateLimiter GamesAddLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter GamesStatsLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter GamesBoosterIdleLimiter = new(TimeSpan.FromSeconds(4));
	private static readonly EndpointRateLimiter GamesCoverLimiter = new(TimeSpan.FromMilliseconds(500));
	private static readonly EndpointRateLimiter AchievementsReadLimiter = new(TimeSpan.FromSeconds(2));
	private static readonly EndpointRateLimiter AchievementsMutateLimiter = new(TimeSpan.FromSeconds(4));
	private static readonly EndpointRateLimiter WishlistReadLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter WishlistWriteLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter DiscoveryQueueReadLimiter = new(TimeSpan.FromSeconds(3));
	private static readonly EndpointRateLimiter DiscoveryQueueExploreLimiter = new(TimeSpan.FromSeconds(8));
	private static readonly EndpointRateLimiter StatusReadLimiter = new(TimeSpan.FromSeconds(1));
	private static readonly EndpointRateLimiter PointsReadLimiter = new(TimeSpan.FromSeconds(2));
	private static readonly EndpointRateLimiter TransferLimiter = new(TimeSpan.FromSeconds(8));
	private static readonly EndpointRateLimiter TradeOffersReadLimiter = new(TimeSpan.FromSeconds(5));
	private static readonly EndpointRateLimiter TradeOffersMutateLimiter = new(TimeSpan.FromSeconds(4));

	[HttpGet("Status")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, PluginStatusResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public ActionResult<GenericResponse> StatusGet([Description("Bot names separated by commas")] string botNames) {
		HashSet<Bot>? bots = GetBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Status", StatusReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, PluginStatusResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = new PluginStatusResponse {
				Version = typeof(ASFBotSocialPlugin).Assembly.GetName().Version?.ToString() ?? "1.0.0",
			};
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, PluginStatusResponse>>(result));
	}

	/// <summary>Steam Points (loyalty rewards) balance.</summary>
	[HttpGet("Points")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, PointsBalanceResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> PointsGet(string botNames, CancellationToken cancellationToken = default) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Points", PointsReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, PointsBalanceResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			cancellationToken.ThrowIfCancellationRequested();

			long? points = null;

			try {
				points = await bot.ArchiHandler.GetPointsBalance().ConfigureAwait(false);
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarning("Points balance failed: " + e.Message);
			}

			result[bot.BotName] = new PointsBalanceResponse { Points = points };
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, PointsBalanceResponse>>(result));
	}

	[HttpGet("Friends")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, FriendsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> FriendsGet(string botNames, CancellationToken cancellationToken = default) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Friends", FriendsReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, FriendsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Friends.ListAsync(bot, cancellationToken).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, FriendsResponse>>(result));
	}

	[HttpPost("Friends/Add")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> FriendsAddPost(string botNames, [FromBody] AddFriendsRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.Targets == null) || (request.Targets.Count == 0)) {
			return BadRequest(new GenericResponse(false, "Targets required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "FriendsAdd", FriendsAddLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Friends.AddAsync(bot, request.Targets, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	[HttpPost("Friends/Remove")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> FriendsRemovePost(string botNames, [FromBody] RemoveFriendsRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.SteamIds == null) || (request.SteamIds.Count == 0)) {
			return BadRequest(new GenericResponse(false, "SteamIds required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "FriendsRemove", FriendsRemoveLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Friends.RemoveAsync(bot, request.SteamIds, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Join Steam community groups by vanity URL, /gid/…, or clan SteamID64.</summary>
	[HttpPost("Groups/Join")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GroupsJoinPost(string botNames, [FromBody] JoinGroupsRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.Targets == null) || (request.Targets.Count == 0)) {
			return BadRequest(new GenericResponse(false, "Targets required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GroupsJoin", GroupsJoinLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Groups.JoinAsync(bot, request.Targets, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Public follower count for the bot profile.</summary>
	[HttpGet("Followers")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, FollowersCountResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> FollowersGet(string botNames, CancellationToken cancellationToken = default) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Followers", FollowersReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, FollowersCountResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Followers.GetCountAsync(bot, cancellationToken).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, FollowersCountResponse>>(result));
	}

	/// <summary>Follow Steam community profiles (workshop/profile follow).</summary>
	[HttpPost("Followers/Follow")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> FollowersFollowPost(string botNames, [FromBody] FollowUsersRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.Targets == null) || (request.Targets.Count == 0)) {
			return BadRequest(new GenericResponse(false, "Targets required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "FollowersFollow", FollowersWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Followers.FollowAsync(bot, request.Targets, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Follow Steam Store curators (mentors) by URL or clan id.</summary>
	[HttpPost("Curators/Follow")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> CuratorsFollowPost(string botNames, [FromBody] FollowCuratorsRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.Targets == null) || (request.Targets.Count == 0)) {
			return BadRequest(new GenericResponse(false, "Targets required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "CuratorsFollow", CuratorsWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Curators.FollowAsync(bot, request.Targets, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Vote on a Steam community review (helpful / unhelpful / funny).</summary>
	[HttpPost("Reviews/Vote")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> ReviewsVotePost(string botNames, [FromBody] VoteReviewRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.Vote)) {
			return BadRequest(new GenericResponse(false, "Url and Vote required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "ReviewsVote", ReviewsWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Reviews.VoteAsync(bot, request.Url, request.Vote, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Vote and/or favorite a Steam shared file (screenshot, artwork, workshop, guide).</summary>
	[HttpPost("SharedFiles/Act")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> SharedFilesActPost(string botNames, [FromBody] SharedFileActionRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.Url)) {
			return BadRequest(new GenericResponse(false, "Url required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "SharedFilesAct", SharedFilesWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await SharedFiles.ActAsync(bot, request.Url, request.Vote, request.Favorite, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	[HttpGet("Games")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GamesResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GamesGet(string botNames, CancellationToken cancellationToken = default) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Games", GamesReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, GamesResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			GamesResponse? games = await Games.ListAsync(bot, cancellationToken).ConfigureAwait(false);

			if (games == null) {
				return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, "Failed to fetch owned games"));
			}

			result[bot.BotName] = games;
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GamesResponse>>(result));
	}

	[HttpGet("Games/Search")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GameSearchResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GamesSearchGet(
		string botNames,
		[FromQuery] string? q = null,
		CancellationToken cancellationToken = default
	) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GamesSearch", GamesSearchLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, GameSearchResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			GameSearchResponse? search = await Games.SearchAsync(bot, q ?? "", cancellationToken).ConfigureAwait(false);

			if (search == null) {
				return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, "Store search unavailable"));
			}

			result[bot.BotName] = search;
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GameSearchResponse>>(result));
	}

	[HttpGet("Games/Stats")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GameStatsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GamesStatsGet(string botNames, CancellationToken cancellationToken = default) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GamesStats", GamesStatsLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, GameStatsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			GameStatsResponse? stats = await Games.StatsAsync(bot, cancellationToken).ConfigureAwait(false);

			if (stats == null) {
				return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, "Games stats unavailable"));
			}

			result[bot.BotName] = stats;
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GameStatsResponse>>(result));
	}

	/// <summary>
	/// Booster pack eligibility apps ranked by playtime for GamesPlayedWhileIdle (max 32).
	/// </summary>
	[HttpGet("Games/BoosterIdleSuggestions")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, BoosterIdleSuggestionsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GamesBoosterIdleSuggestionsGet(string botNames, CancellationToken cancellationToken = default) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GamesBoosterIdle", GamesBoosterIdleLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, BoosterIdleSuggestionsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			BoosterIdleSuggestionsResponse? payload = await Games.BoosterIdleSuggestionsAsync(bot, cancellationToken).ConfigureAwait(false);

			if (payload == null) {
				return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, "Booster eligibility unavailable"));
			}

			result[bot.BotName] = payload;
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, BoosterIdleSuggestionsResponse>>(result));
	}

	/// <summary>Resolve store artwork URLs (hashed CDN) when classic steam/apps paths 404.</summary>
	[HttpGet("Games/{appId:int}/Cover")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GameCoverResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GameCoverGet(string botNames, uint appId, CancellationToken cancellationToken = default) {
		if (appId == 0) {
			return BadRequest(new GenericResponse(false, "AppID required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		if (bots.Count != 1) {
			return BadRequest(new GenericResponse(false, "Cover resolve accepts exactly one bot"));
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GameCover", GamesCoverLimiter);

		if (limited != null) {
			return limited;
		}

		Bot bot = bots.First();
		GameCoverResponse? cover = await Games.ResolveCoverAsync(bot, appId, cancellationToken).ConfigureAwait(false);

		Dictionary<string, GameCoverResponse> result = new(1, BotNameComparer) {
			[bot.BotName] = cover ?? new GameCoverResponse { AppId = appId },
		};

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GameCoverResponse>>(result));
	}

	/// <summary>List achievements for one owned game (schema + unlock state).</summary>
	[HttpGet("Games/{appId:int}/Achievements")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, GameAchievementsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GameAchievementsGet(string botNames, uint appId, CancellationToken cancellationToken = default) {
		if (appId == 0) {
			return BadRequest(new GenericResponse(false, "AppID required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		if (bots.Count != 1) {
			return BadRequest(new GenericResponse(false, "Achievements list accepts exactly one bot"));
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GameAchievements", AchievementsReadLimiter);

		if (limited != null) {
			return limited;
		}

		Bot bot = bots.First();
		GameAchievementsResponse? payload = await Achievements.ListAsync(bot, appId, cancellationToken).ConfigureAwait(false);

		if (payload == null) {
			return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, "Achievements unavailable"));
		}

		Dictionary<string, GameAchievementsResponse> result = new(1, BotNameComparer) {
			[bot.BotName] = payload,
		};

		return Ok(new GenericResponse<IReadOnlyDictionary<string, GameAchievementsResponse>>(result));
	}

	/// <summary>Unlock selected (or all unlockable) achievements — same Steam protocol path as SAM.</summary>
	[HttpPost("Games/{appId:int}/Achievements/Unlock")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, AchievementMutationResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GameAchievementsUnlockPost(string botNames, uint appId, [FromBody] SetAchievementsRequest request) {
		return await MutateAchievementsAsync(botNames, appId, request, unlock: true).ConfigureAwait(false);
	}

	/// <summary>Lock selected (or all unlockable) achievements.</summary>
	[HttpPost("Games/{appId:int}/Achievements/Lock")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, AchievementMutationResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GameAchievementsLockPost(string botNames, uint appId, [FromBody] SetAchievementsRequest request) {
		return await MutateAchievementsAsync(botNames, appId, request, unlock: false).ConfigureAwait(false);
	}

	[HttpPost("Games/Add")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> GamesAddPost(string botNames, [FromBody] AddGamesRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.AppIds == null) || (request.AppIds.Count == 0)) {
			return BadRequest(new GenericResponse(false, "AppIds required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "GamesAdd", GamesAddLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Games.AddAsync(bot, request.AppIds, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	[HttpGet("Wishlist")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, WishlistResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> WishlistGet(string botNames) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Wishlist", WishlistReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, WishlistResponse> result = new(bots.Count, BotNameComparer);
		List<string> failures = [];

		foreach (Bot bot in bots) {
			try {
				WishlistResponse? wishlist = await Wishlist.ListAsync(bot).ConfigureAwait(false);

				if (wishlist == null) {
					failures.Add(bot.BotName);
					result[bot.BotName] = new WishlistResponse();

					continue;
				}

				result[bot.BotName] = wishlist;
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);
				failures.Add(bot.BotName);
				result[bot.BotName] = new WishlistResponse();
			}
		}

		if ((failures.Count > 0) && (failures.Count == bots.Count)) {
			return StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, "Wishlist unavailable (Steam API). Try again later."));
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, WishlistResponse>>(result));
	}

	[HttpPost("Wishlist/Add")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> WishlistAddPost(string botNames, [FromBody] WishlistMutationRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.AppIds == null) || (request.AppIds.Count == 0)) {
			return BadRequest(new GenericResponse(false, "AppIds required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "WishlistAdd", WishlistWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Wishlist.AddAsync(bot, request.AppIds, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Add a store app to wishlist and follow it (skips actions already done).</summary>
	[HttpPost("Wishlist/FollowAndAdd")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> WishlistFollowAndAddPost(string botNames, [FromBody] WishlistFollowRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.Url)) {
			return BadRequest(new GenericResponse(false, "Url required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "WishlistFollowAndAdd", WishlistWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Wishlist.FollowAndWishlistAsync(bot, request.Url, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Follow a store app only (idempotent).</summary>
	[HttpPost("Wishlist/Follow")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> WishlistFollowPost(string botNames, [FromBody] WishlistFollowRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.Url)) {
			return BadRequest(new GenericResponse(false, "Url required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "WishlistFollow", WishlistWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Wishlist.FollowGameOnlyAsync(bot, request.Url, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	[HttpPost("Wishlist/Remove")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> WishlistRemovePost(string botNames, [FromBody] WishlistMutationRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.AppIds == null) || (request.AppIds.Count == 0)) {
			return BadRequest(new GenericResponse(false, "AppIds required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "WishlistRemove", WishlistWriteLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, MutationsResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await Wishlist.RemoveAsync(bot, request.AppIds, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, MutationsResponse>>(result));
	}

	/// <summary>Steam discovery queue status (daily explore / sale cards wording on /explore).</summary>
	[HttpGet("DiscoveryQueue")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, DiscoveryQueueStatusResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> DiscoveryQueueGet(string botNames) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "DiscoveryQueue", DiscoveryQueueReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, DiscoveryQueueStatusResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await DiscoveryQueue.GetStatusAsync(bot, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, DiscoveryQueueStatusResponse>>(result));
	}

	/// <summary>Generate and clear Steam discovery queue(s). Works even after the daily pass (Queues 1–3).</summary>
	[HttpPost("DiscoveryQueue/Explore")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, DiscoveryQueueExploreResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> DiscoveryQueueExplorePost(string botNames, [FromBody] DiscoveryQueueExploreRequest? request) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "DiscoveryQueueExplore", DiscoveryQueueExploreLimiter);

		if (limited != null) {
			return limited;
		}

		byte queues = request?.Queues ?? 1;

		if (queues is 0 or > 3) {
			return BadRequest(new GenericResponse(false, "Queues must be between 1 and 3"));
		}

		Dictionary<string, DiscoveryQueueExploreResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await DiscoveryQueue.ExploreAsync(bot, queues, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, DiscoveryQueueExploreResponse>>(result));
	}

	/// <summary>Pending trade offers (active + needs mobile confirmation) for the bot.</summary>
	[HttpGet("TradeOffers")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, PendingTradeOffersResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> TradeOffersGet(string botNames) {
		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "TradeOffers", TradeOffersReadLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, PendingTradeOffersResponse> result = new(bots.Count, BotNameComparer);

		foreach (Bot bot in bots) {
			result[bot.BotName] = await TradeOffers.ListPendingAsync(bot, HttpContext.RequestAborted).ConfigureAwait(false);
		}

		return Ok(new GenericResponse<IReadOnlyDictionary<string, PendingTradeOffersResponse>>(result));
	}

	/// <summary>Cancel a sent trade offer (incl. needs confirmation) or decline a received one.</summary>
	[HttpPost("TradeOffers/Cancel")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, TradeOfferActionResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> TradeOffersCancelPost(string botNames, [FromBody] CancelTradeOfferRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.TradeOfferId)) {
			return BadRequest(new GenericResponse(false, "TradeOfferId required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		if (bots.Count != 1) {
			return BadRequest(new GenericResponse(false, "Trade offer cancel accepts exactly one bot"));
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "TradeOffers/Cancel", TradeOffersMutateLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, TradeOfferActionResponse> result = new(1, BotNameComparer);
		Bot bot = bots.First();
		result[bot.BotName] = await TradeOffers.CancelOrDeclineAsync(bot, request, HttpContext.RequestAborted).ConfigureAwait(false);

		return Ok(new GenericResponse<IReadOnlyDictionary<string, TradeOfferActionResponse>>(result));
	}

	/// <summary>Transfer selected Steam inventory items to another ASF bot in the farm (trade offer).</summary>
	[HttpPost("Inventory/Transfer")]
	[ProducesResponseType<GenericResponse<IReadOnlyDictionary<string, TransferResponse>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.ServiceUnavailable)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.TooManyRequests)]
	public async Task<ActionResult<GenericResponse>> InventoryTransferPost(string botNames, [FromBody] TransferRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		if ((request.AssetIds == null) || (request.AssetIds.Count == 0)) {
			return BadRequest(new GenericResponse(false, "AssetIds required"));
		}

		if (string.IsNullOrWhiteSpace(request.TargetBotName)) {
			return BadRequest(new GenericResponse(false, "TargetBotName required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		// Multi-bot transfer in one call is risky; P0 allows one source bot per request.
		if (bots.Count != 1) {
			return BadRequest(new GenericResponse(false, "Inventory transfer accepts exactly one source bot"));
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, "Inventory/Transfer", TransferLimiter);

		if (limited != null) {
			return limited;
		}

		Dictionary<string, TransferResponse> result = new(1, BotNameComparer);
		Bot source = bots.First();
		result[source.BotName] = await InventoryTransfer.TransferToBotAsync(source, request, HttpContext.RequestAborted).ConfigureAwait(false);

		return Ok(new GenericResponse<IReadOnlyDictionary<string, TransferResponse>>(result));
	}

	private async Task<ActionResult<GenericResponse>> MutateAchievementsAsync(string botNames, uint appId, SetAchievementsRequest request, bool unlock) {
		ArgumentNullException.ThrowIfNull(request);

		if (appId == 0) {
			return BadRequest(new GenericResponse(false, "AppID required"));
		}

		if (!request.All && ((request.Indices == null) || (request.Indices.Count == 0))) {
			return BadRequest(new GenericResponse(false, "Indices or All required"));
		}

		HashSet<Bot>? bots = GetConnectedBotsOrNull(botNames, out ActionResult<GenericResponse>? error);

		if (bots == null) {
			return error!;
		}

		if (bots.Count != 1) {
			return BadRequest(new GenericResponse(false, "Achievement mutations accept exactly one bot"));
		}

		ActionResult<GenericResponse>? limited = AcquireForBots(bots, unlock ? "GameAchievements/Unlock" : "GameAchievements/Lock", AchievementsMutateLimiter);

		if (limited != null) {
			return limited;
		}

		Bot bot = bots.First();
		AchievementMutationResponse payload = await Achievements.SetAsync(
			bot,
			appId,
			request.Indices,
			request.All,
			unlock,
			HttpContext.RequestAborted
		).ConfigureAwait(false);

		Dictionary<string, AchievementMutationResponse> result = new(1, BotNameComparer) {
			[bot.BotName] = payload,
		};

		return Ok(new GenericResponse<IReadOnlyDictionary<string, AchievementMutationResponse>>(result));
	}

	private static ActionResult<GenericResponse>? AcquireForBots(IEnumerable<Bot> bots, string endpoint, EndpointRateLimiter limiter) {
		foreach (Bot bot in bots) {
			ActionResult<GenericResponse>? limited = limiter.TryAcquire(bot.BotName, endpoint);

			if (limited != null) {
				return limited;
			}
		}

		return null;
	}

	private static HashSet<Bot>? GetBotsOrNull(string botNames, out ActionResult<GenericResponse>? error) {
		error = null;
		ArgumentException.ThrowIfNullOrEmpty(botNames);

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			error = new BadRequestObjectResult(new GenericResponse(false, $"Bot not found: {botNames}"));

			return null;
		}

		return bots;
	}

	private static HashSet<Bot>? GetConnectedBotsOrNull(string botNames, out ActionResult<GenericResponse>? error) {
		HashSet<Bot>? bots = GetBotsOrNull(botNames, out error);

		if (bots == null) {
			return null;
		}

		if (bots.Any(static bot => !bot.IsConnectedAndLoggedOn)) {
			error = new ObjectResult(new GenericResponse(false, "Bot is not connected")) {
				StatusCode = (int) HttpStatusCode.ServiceUnavailable,
			};

			return null;
		}

		return bots;
	}
}
