using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using ASFBotSocial.Models;

namespace ASFBotSocial.Services;

internal enum OwnReviewsError : byte {
	None,
	NotConnected,
	SessionExpired,
	NotFound,
	SteamUnavailable,
	UnexpectedPage,
	InvalidRequest,
	AlreadyReviewed,
	RatingResetUnconfirmed,
	SteamRejected,
	Busy,
}

internal sealed class OwnReviewsResult<T> where T : class {
	internal T? Value { get; init; }
	internal OwnReviewsError Error { get; init; }
	internal string Message { get; init; } = "";

	internal static OwnReviewsResult<T> Ok(T value) => new() { Value = value };

	internal static OwnReviewsResult<T> Fail(OwnReviewsError error, string message) => new() { Error = error, Message = message };
}

/// <summary>
/// Access to the reviews written by a bot account: list, detail, editor state, publish and edit.
/// The author is always the bot's own SteamID: no endpoint accepts an author, so a caller can never read
/// or modify another account's reviews through this service.
/// Sources (see the README): reads parse the public community review pages; edit posts to the same
/// <c>/userreviews/update/{id}</c> call Steam's own edit form uses on the author's review page; publish posts to the
/// store's <c>/friends/recommendgame</c> call. Delete is intentionally not implemented.
/// </summary>
internal sealed class OwnReviewsService {
	/// <summary>Steam returns a fixed 10 entries per page on /recommended/.</summary>
	internal const uint SteamPageSize = 10;

	private const uint MaxPage = 500;
	private const int ConfirmAttempts = 3;

	// Language ids offered by Steam's own review form (store page, "Language" drop-down).
	private static readonly HashSet<string> ReviewLanguages = new(StringComparer.Ordinal) {
		"schinese", "tchinese", "japanese", "koreana", "thai", "indonesian", "malay", "bulgarian", "czech", "danish", "german", "english",
		"spanish", "latam", "greek", "french", "italian", "hungarian", "dutch", "norwegian", "polish", "portuguese", "brazilian", "romanian",
		"russian", "finnish", "swedish", "turkish", "vietnamese", "ukrainian",
	};

	private static readonly Regex ControlCharsRegex = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	// One write at a time per bot: a double click can never run two publishes / edits in parallel.
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);

	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(1.5));

	public async Task<OwnReviewsResult<OwnReviewsResponse>> ListAsync(Bot bot, uint page, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (page is 0 or > MaxPage) {
			page = 1;
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return OwnReviewsResult<OwnReviewsResponse>.Fail(OwnReviewsError.NotConnected, "Bot is not connected");
		}

		ulong steamId = bot.SteamID;
		Uri request = new(ArchiWebHandler.SteamCommunityURL, string.Create(CultureInfo.InvariantCulture, $"/profiles/{steamId}/recommended/?l=english&p={page}"));
		(string? html, OwnReviewsResult<OwnReviewsResponse>? failure) = await FetchAsync<OwnReviewsResponse>(bot, request, cancellationToken).ConfigureAwait(false);

		if (html == null) {
			return failure!;
		}

		OwnReviewsParser.ListPage parsed = OwnReviewsParser.ParseList(html, steamId);

		if (parsed.IsErrorPage) {
			return OwnReviewsResult<OwnReviewsResponse>.Fail(OwnReviewsError.UnexpectedPage, "Steam returned an error page for the profile review list");
		}

		uint? total = parsed.Total;
		uint end = parsed.RangeEnd ?? ((page - 1) * SteamPageSize + (uint) parsed.Items.Count);
		bool hasMore = total.HasValue ? end < total.Value : parsed.Items.Count >= SteamPageSize;
		bool unconfirmedEmpty = parsed.Items.Count == 0 && total == null && page == 1;

		return OwnReviewsResult<OwnReviewsResponse>.Ok(new OwnReviewsResponse {
			SteamId = steamId.ToString(CultureInfo.InvariantCulture),
			Page = page,
			PageSize = SteamPageSize,
			Total = total,
			HasMore = hasMore,
			// "Complete" is only claimed when Steam gave us the total and this page reaches it, or when
			// the last page returned fewer entries than a full page.
			Complete = !hasMore && !unconfirmedEmpty,
			Status = unconfirmedEmpty ? "empty_unconfirmed" : "ok",
			Items = parsed.Items,
		});
	}

	public async Task<OwnReviewsResult<OwnReviewDetailResponse>> GetAsync(Bot bot, uint appId, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!bot.IsConnectedAndLoggedOn) {
			return OwnReviewsResult<OwnReviewDetailResponse>.Fail(OwnReviewsError.NotConnected, "Bot is not connected");
		}

		(string? html, OwnReviewsResult<OwnReviewDetailResponse>? failure) = await FetchReviewPageAsync<OwnReviewDetailResponse>(bot, appId, cancellationToken).ConfigureAwait(false);

		if (html == null) {
			return failure!;
		}

		OwnReviewDetailResponse? detail = OwnReviewsParser.ParseDetail(html, bot.SteamID, appId);

		return detail == null
			? OwnReviewsResult<OwnReviewDetailResponse>.Fail(OwnReviewsError.NotFound, "This account has no review for that app")
			: OwnReviewsResult<OwnReviewDetailResponse>.Ok(detail);
	}

	/// <summary>Raw text and settings Steam's edit form holds, so the editor starts from what is really stored.</summary>
	public async Task<OwnReviewsResult<OwnReviewEditorResponse>> GetEditorAsync(Bot bot, uint appId, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!bot.IsConnectedAndLoggedOn) {
			return OwnReviewsResult<OwnReviewEditorResponse>.Fail(OwnReviewsError.NotConnected, "Bot is not connected");
		}

		(string? html, OwnReviewsResult<OwnReviewEditorResponse>? failure) = await FetchReviewPageAsync<OwnReviewEditorResponse>(bot, appId, cancellationToken).ConfigureAwait(false);

		if (html == null) {
			return failure!;
		}

		(OwnReviewsParser.EditorState? state, OwnReviewsError error, string message) = ReadEditor(html, bot.SteamID, appId);

		if (state == null) {
			return OwnReviewsResult<OwnReviewEditorResponse>.Fail(error, message);
		}

		return OwnReviewsResult<OwnReviewEditorResponse>.Ok(new OwnReviewEditorResponse {
			SteamId = bot.SteamID.ToString(CultureInfo.InvariantCulture),
			AppId = appId,
			ReviewId = state.ReviewId,
			Url = OwnReviewsParser.ReviewUrl(bot.SteamID, appId),
			Recommended = state.Recommended,
			Text = state.Text,
			ReceivedCompensation = state.ReceivedCompensation,
		});
	}

	public async Task<OwnReviewsResult<OwnReviewWriteResponse>> PublishAsync(Bot bot, OwnReviewPublishRequest request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(request);

		if (request.AppId == 0) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.InvalidRequest, "AppID required");
		}

		string? invalid = ValidateReview(request.Recommended, request.Text, out string text);

		if (invalid != null) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.InvalidRequest, invalid);
		}

		string language = string.IsNullOrWhiteSpace(request.Language) ? "english" : request.Language.Trim().ToLowerInvariant();

		if (!ReviewLanguages.Contains(language)) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.InvalidRequest, "Unknown review language");
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.NotConnected, "Bot is not connected");
		}

		uint appId = request.AppId;
		bool recommended = request.Recommended!.Value;

		using IDisposable? gate = await EnterAsync(bot, cancellationToken).ConfigureAwait(false);

		if (gate == null) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.Busy, "Another review operation is still running for this bot");
		}

		(string? html, OwnReviewsResult<OwnReviewWriteResponse>? failure) = await FetchReviewPageAsync<OwnReviewWriteResponse>(bot, appId, cancellationToken).ConfigureAwait(false);

		if (html == null) {
			return failure!;
		}

		// A second review for the same app would silently overwrite the first one on Steam's side.
		if (OwnReviewsParser.ParseDetail(html, bot.SteamID, appId) != null) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.AlreadyReviewed, "This account already reviewed that app; edit the existing review instead");
		}

		string appIdText = appId.ToString(CultureInfo.InvariantCulture);
		Uri post = new(ArchiWebHandler.SteamStoreURL, "/friends/recommendgame");
		Dictionary<string, string> data = new(9, StringComparer.Ordinal) {
			["appid"] = appIdText,
			["steamworksappid"] = appIdText,
			["comment"] = text,
			["rated_up"] = recommended ? "true" : "false",
			["is_public"] = "true",
			["language"] = language,
			["received_compensation"] = "0",
			["disable_comments"] = "0",
			["saved_hardware_id"] = "0",
		};

		OwnReviewsResult<OwnReviewWriteResponse>? rejected = await PostAsync<OwnReviewWriteResponse>(bot, post, data, new Uri(ArchiWebHandler.SteamStoreURL, $"/app/{appIdText}/"), cancellationToken).ConfigureAwait(false);

		if (rejected != null) {
			return rejected;
		}

		bot.ArchiLogger.LogGenericInfo($"OwnReviews: published a review for app {appIdText}");

		(bool verified, OwnReviewDetailResponse? review) = await ConfirmAsync(bot, appId, recommended, text, cancellationToken).ConfigureAwait(false);

		return OwnReviewsResult<OwnReviewWriteResponse>.Ok(new OwnReviewWriteResponse { Operation = "published", AppId = appId, Verified = verified, Review = review });
	}

	public async Task<OwnReviewsResult<OwnReviewWriteResponse>> EditAsync(Bot bot, uint appId, OwnReviewEditRequest request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(request);

		if (appId == 0) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.InvalidRequest, "AppID required");
		}

		string? invalid = ValidateReview(request.Recommended, request.Text, out string text);

		if (invalid != null) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.InvalidRequest, invalid);
		}

		if (!bot.IsConnectedAndLoggedOn) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.NotConnected, "Bot is not connected");
		}

		bool recommended = request.Recommended!.Value;

		using IDisposable? gate = await EnterAsync(bot, cancellationToken).ConfigureAwait(false);

		if (gate == null) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.Busy, "Another review operation is still running for this bot");
		}

		(string? html, OwnReviewsResult<OwnReviewWriteResponse>? failure) = await FetchReviewPageAsync<OwnReviewWriteResponse>(bot, appId, cancellationToken).ConfigureAwait(false);

		if (html == null) {
			return failure!;
		}

		// The page is requested for the bot's own profile and Steam renders the edit form only for the author:
		// without it there is nothing this bot may edit.
		(OwnReviewsParser.EditorState? state, OwnReviewsError error, string message) = ReadEditor(html, bot.SteamID, appId);

		if (state == null) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(error, message);
		}

		if (state.Recommended != recommended && !request.ConfirmRatingReset) {
			return OwnReviewsResult<OwnReviewWriteResponse>.Fail(OwnReviewsError.RatingResetUnconfirmed, "Changing the recommendation makes Steam clear the ratings other users gave this review; confirm it to continue");
		}

		if (state.Recommended == recommended && string.Equals(state.Text.Trim(), text, StringComparison.Ordinal)) {
			// Nothing to save: do not touch Steam (it would only stamp a new "last edited" date).
			OwnReviewDetailResponse? current = OwnReviewsParser.ParseDetail(html, bot.SteamID, appId);

			return OwnReviewsResult<OwnReviewWriteResponse>.Ok(new OwnReviewWriteResponse { Operation = "edited", AppId = appId, Verified = true, Review = current });
		}

		// Same fields and endpoint as the "Save Changes" button of Steam's own edit form.
		Uri post = new(ArchiWebHandler.SteamCommunityURL, string.Create(CultureInfo.InvariantCulture, $"/userreviews/update/{state.ReviewId}"));
		Dictionary<string, string> data = new(4, StringComparer.Ordinal) {
			["review_text"] = text,
			["voted_up"] = recommended ? "true" : "false",
			["received_compensation"] = state.ReceivedCompensation ? "true" : "false",
			["saved_hardware_id"] = state.SavedHardwareId,
		};

		OwnReviewsResult<OwnReviewWriteResponse>? rejected = await PostAsync<OwnReviewWriteResponse>(bot, post, data, new Uri(ArchiWebHandler.SteamCommunityURL, string.Create(CultureInfo.InvariantCulture, $"/profiles/{bot.SteamID}/recommended/{appId}/")), cancellationToken).ConfigureAwait(false);

		if (rejected != null) {
			return rejected;
		}

		bot.ArchiLogger.LogGenericInfo(string.Create(CultureInfo.InvariantCulture, $"OwnReviews: edited the review for app {appId}"));

		(bool verified, OwnReviewDetailResponse? review) = await ConfirmAsync(bot, appId, recommended, text, cancellationToken).ConfigureAwait(false);

		return OwnReviewsResult<OwnReviewWriteResponse>.Ok(new OwnReviewWriteResponse { Operation = "edited", AppId = appId, Verified = verified, Review = review });
	}

	private static (OwnReviewsParser.EditorState? State, OwnReviewsError Error, string Message) ReadEditor(string html, ulong steamId, uint appId) {
		OwnReviewsParser.EditorState? state = OwnReviewsParser.ParseEditor(html);

		if (state != null) {
			return (state, OwnReviewsError.None, "");
		}

		return OwnReviewsParser.ParseDetail(html, steamId, appId) == null
			? (null, OwnReviewsError.NotFound, "This account has no review for that app")
			: (null, OwnReviewsError.UnexpectedPage, "Steam did not render the edit form for this account");
	}

	private static string? ValidateReview(bool? recommended, string? rawText, out string text) {
		text = OwnReviewsParser.NormalizeNewlines(rawText ?? "").Trim();

		if (recommended == null) {
			return "Recommended required (true or false)";
		}

		if (text.Length == 0) {
			return "Review text required";
		}

		if (text.Length > OwnReviewEditorResponse.MaxTextLength) {
			return string.Create(CultureInfo.InvariantCulture, $"Review text too long (maximum {OwnReviewEditorResponse.MaxTextLength} characters)");
		}

		return ControlCharsRegex.IsMatch(text) ? "Review text contains control characters" : null;
	}

	/// <summary>Returns a gate released on dispose, or null when another write for the same bot is running.</summary>
	private static async Task<IDisposable?> EnterAsync(Bot bot, CancellationToken cancellationToken) {
		SemaphoreSlim semaphore = WriteGates.GetOrAdd(bot.BotName, static _ => new SemaphoreSlim(1, 1));

		return await semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false) ? new Releaser(semaphore) : null;
	}

	/// <summary>Posts a review write. Returns null when Steam accepted it, otherwise the failure to hand back.</summary>
	private static async Task<OwnReviewsResult<T>?> PostAsync<T>(Bot bot, Uri request, Dictionary<string, string> data, Uri referer, CancellationToken cancellationToken) where T : class {
		try {
			ObjectResponse<JsonNode>? response = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<JsonNode>(
				request,
				data: data,
				referer: referer,
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.ReturnServerErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
				maxTries: 1,
				cancellationToken: cancellationToken
			).ConfigureAwait(false);

			JsonNode? body = response?.Content;

			if (body == null) {
				return OwnReviewsResult<T>.Fail(OwnReviewsError.SteamUnavailable, "Steam did not answer the review request");
			}

			if (IsSuccess(body)) {
				return null;
			}

			return OwnReviewsResult<T>.Fail(OwnReviewsError.SteamRejected, SteamErrorText(body));
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return OwnReviewsResult<T>.Fail(OwnReviewsError.SteamUnavailable, "Could not reach Steam: " + e.Message);
		}
	}

	private static bool IsSuccess(JsonNode body) {
		JsonNode? success = body is JsonObject ? body["success"] : body;

		if (success is not JsonValue value) {
			return false;
		}

		if (value.TryGetValue(out bool flag)) {
			return flag;
		}

		if (value.TryGetValue(out int number)) {
			return number == 1;
		}

		return value.TryGetValue(out string? text) && text is "1" or "true";
	}

	private static string SteamErrorText(JsonNode body) {
		string? message = null;

		if (body is JsonObject obj) {
			if (obj["strError"] is JsonValue str && str.TryGetValue(out string? error)) {
				message = error;
			} else if (obj["success"] is JsonValue code && code.TryGetValue(out int number)) {
				message = string.Create(CultureInfo.InvariantCulture, $"Steam refused the request (code {number})");
			}
		}

		if (string.IsNullOrWhiteSpace(message)) {
			return "Steam refused the request";
		}

		string clean = Regex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(message, " ")), @"\s+", " ").Trim();

		return clean.Length > 300 ? clean[..300] + "…" : clean;
	}

	/// <summary>Reads the review back from Steam to confirm what it stored (it can lag a moment after the write).</summary>
	private async Task<(bool Verified, OwnReviewDetailResponse? Review)> ConfirmAsync(Bot bot, uint appId, bool recommended, string text, CancellationToken cancellationToken) {
		OwnReviewDetailResponse? last = null;

		for (int attempt = 0; attempt < ConfirmAttempts; attempt++) {
			if (attempt > 0) {
				await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
			}

			(string? html, _) = await FetchReviewPageAsync<OwnReviewDetailResponse>(bot, appId, cancellationToken).ConfigureAwait(false);

			if (html == null) {
				continue;
			}

			OwnReviewDetailResponse? detail = OwnReviewsParser.ParseDetail(html, bot.SteamID, appId);
			OwnReviewsParser.EditorState? state = OwnReviewsParser.ParseEditor(html);

			if (detail != null) {
				last = detail;
			}

			if (detail != null && state != null && state.Recommended == recommended && string.Equals(state.Text.Trim(), text, StringComparison.Ordinal)) {
				return (true, detail);
			}
		}

		return (false, last);
	}

	private Task<(string? Html, OwnReviewsResult<T>? Failure)> FetchReviewPageAsync<T>(Bot bot, uint appId, CancellationToken cancellationToken) where T : class {
		Uri request = new(ArchiWebHandler.SteamCommunityURL, string.Create(CultureInfo.InvariantCulture, $"/profiles/{bot.SteamID}/recommended/{appId}/?l=english"));

		return FetchAsync<T>(bot, request, cancellationToken);
	}

	private async Task<(string? Html, OwnReviewsResult<T>? Failure)> FetchAsync<T>(Bot bot, Uri request, CancellationToken cancellationToken) where T : class {
		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			// Binary fetch: the plugin does not bind AngleSharp (not always loadable from plugins/).
			BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(request).ConfigureAwait(false);

			if (response?.Content == null || response.Content.Count == 0) {
				return (null, OwnReviewsResult<T>.Fail(OwnReviewsError.SteamUnavailable, "Steam did not answer the review request"));
			}

			string path = response.FinalUri?.AbsolutePath ?? "";

			if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)) {
				return (null, OwnReviewsResult<T>.Fail(OwnReviewsError.SessionExpired, "Steam session expired; the bot must reconnect"));
			}

			return (Encoding.UTF8.GetString([.. response.Content]), null);
		} catch (OperationCanceledException) {
			throw;
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return (null, OwnReviewsResult<T>.Fail(OwnReviewsError.SteamUnavailable, "Could not reach Steam: " + e.Message));
		}
	}

	// 401 is avoided on purpose: ASF uses it for IPC authentication and clients may treat it as a logout.
	internal static HttpStatusCode ToStatusCode(OwnReviewsError error) => error switch {
		OwnReviewsError.NotFound => HttpStatusCode.NotFound,
		OwnReviewsError.InvalidRequest => HttpStatusCode.BadRequest,
		OwnReviewsError.AlreadyReviewed or OwnReviewsError.RatingResetUnconfirmed or OwnReviewsError.Busy => HttpStatusCode.Conflict,
		OwnReviewsError.SteamRejected => HttpStatusCode.UnprocessableEntity,
		_ => HttpStatusCode.ServiceUnavailable,
	};

	internal static string ErrorCode(OwnReviewsError error) => error switch {
		OwnReviewsError.NotConnected => "bot_not_connected",
		OwnReviewsError.SessionExpired => "session_expired",
		OwnReviewsError.NotFound => "review_not_found",
		OwnReviewsError.UnexpectedPage => "steam_unexpected_page",
		OwnReviewsError.InvalidRequest => "review_invalid",
		OwnReviewsError.AlreadyReviewed => "review_exists",
		OwnReviewsError.RatingResetUnconfirmed => "rating_reset_unconfirmed",
		OwnReviewsError.SteamRejected => "steam_rejected",
		OwnReviewsError.Busy => "operation_in_progress",
		_ => "steam_unavailable",
	};

	private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable {
		public void Dispose() => semaphore.Release();
	}
}
