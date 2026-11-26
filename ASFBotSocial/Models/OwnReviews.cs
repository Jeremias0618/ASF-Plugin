using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ASFBotSocial.Models;

/// <summary>Reaction award shown by Steam on a review (only what Steam returns).</summary>
public sealed class OwnReviewAward {
	[JsonInclude]
	[Description("Steam reaction id when present in the page")]
	public uint? Id { get; init; }

	[JsonInclude]
	public string Name { get; init; } = "";

	[JsonInclude]
	public string? IconUrl { get; init; }

	[JsonInclude]
	[Description("Times the award was given; null when Steam did not return it")]
	public uint? Count { get; init; }
}

/// <summary>One review written by the bot account, as listed on its Steam community profile.</summary>
public sealed class OwnReviewEntry {
	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	[Description("Steam review id (64-bit safe string). Null when the page did not expose it for the author")]
	public string? ReviewId { get; init; }

	[JsonInclude]
	public string Url { get; init; } = "";

	[JsonInclude]
	[Description("Author's recommendation: true = recommended, false = not recommended, null = unknown")]
	public bool? Recommended { get; init; }

	[JsonInclude]
	public string Excerpt { get; init; } = "";

	[JsonInclude]
	[Description("True when Excerpt was cut by the plugin (full text is available in the detail)")]
	public bool ExcerptTruncated { get; init; }

	[JsonInclude]
	[Description("Date text exactly as Steam returned it (English)")]
	public string? PostedText { get; init; }

	[JsonInclude]
	[Description("yyyy-MM-dd when PostedText could be parsed; Steam list pages do not include a time")]
	public string? PostedDate { get; init; }

	[JsonInclude]
	public string? PlaytimeText { get; init; }

	[JsonInclude]
	public string? CoverUrl { get; init; }

	[JsonInclude]
	[Description("Received 'helpful' votes. 0 only when Steam stated that nobody rated it; null = not returned")]
	public uint? HelpfulCount { get; init; }

	[JsonInclude]
	[Description("Received 'funny' votes; null when Steam did not return it")]
	public uint? FunnyCount { get; init; }

	[JsonInclude]
	public IReadOnlyList<OwnReviewAward> Awards { get; init; } = [];
}

/// <summary>Page of the author's reviews.</summary>
public sealed class OwnReviewsResponse {
	[JsonInclude]
	[Description("Author SteamID64 (string, derived from the bot session)")]
	public string SteamId { get; init; } = "";

	[JsonInclude]
	public uint Page { get; init; } = 1;

	[JsonInclude]
	[Description("Page size used by Steam (fixed by Steam)")]
	public uint PageSize { get; init; }

	[JsonInclude]
	[Description("Total entries stated by Steam ('Showing x-y of N'); null when Steam did not state it")]
	public uint? Total { get; init; }

	[JsonInclude]
	public bool HasMore { get; init; }

	[JsonInclude]
	[Description("True when this page reaches the end of the author's list")]
	public bool Complete { get; init; }

	[JsonInclude]
	[Description("ok | empty_unconfirmed (Steam returned no reviews; could be none or an unreadable profile)")]
	public string Status { get; init; } = "ok";

	[JsonInclude]
	public IReadOnlyList<OwnReviewEntry> Items { get; init; } = [];
}

/// <summary>Detail of one review written by the bot account.</summary>
public sealed class OwnReviewDetailResponse {
	[JsonInclude]
	public string SteamId { get; init; } = "";

	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	public string? ReviewId { get; init; }

	[JsonInclude]
	public string Url { get; init; } = "";

	[JsonInclude]
	public string? Title { get; init; }

	[JsonInclude]
	public bool? Recommended { get; init; }

	[JsonInclude]
	[Description("Full review text (plain text; Steam markup removed)")]
	public string Text { get; init; } = "";

	[JsonInclude]
	public string? PostedText { get; init; }

	[JsonInclude]
	public string? PlaytimeText { get; init; }

	[JsonInclude]
	public uint? HelpfulCount { get; init; }

	[JsonInclude]
	public uint? FunnyCount { get; init; }

	[JsonInclude]
	public IReadOnlyList<OwnReviewAward> Awards { get; init; } = [];
}

/// <summary>Editable state of the review the bot account wrote for one app (what Steam's own edit form holds).</summary>
public sealed class OwnReviewEditorResponse {
	/// <summary>Steam's review text limit (the maxlength of its edit form).</summary>
	internal const int MaxTextLength = 8000;

	[JsonInclude]
	public string SteamId { get; init; } = "";

	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	[Description("Steam review id (64-bit safe string)")]
	public string ReviewId { get; init; } = "";

	[JsonInclude]
	public string Url { get; init; } = "";

	[JsonInclude]
	[Description("Current recommendation: true = recommended, false = not recommended")]
	public bool Recommended { get; init; }

	[JsonInclude]
	[Description("Full review text exactly as Steam stores it (Steam markup preserved, not stripped)")]
	public string Text { get; init; } = "";

	[JsonInclude]
	public bool ReceivedCompensation { get; init; }

	[JsonInclude]
	public uint MaxLength { get; init; } = MaxTextLength;
}

/// <summary>Publish a new review for the bot account. The author is always the bot of the route.</summary>
public sealed class OwnReviewPublishRequest {
	[JsonInclude]
	[Description("App the review is for")]
	public uint AppId { get; init; }

	[JsonInclude]
	[Description("true = recommended, false = not recommended (required)")]
	public bool? Recommended { get; init; }

	[JsonInclude]
	[Description("Review text, 1-8000 characters")]
	public string? Text { get; init; }

	[JsonInclude]
	[Description("Steam review language id (e.g. english, spanish, latam). Defaults to english")]
	public string? Language { get; init; }
}

/// <summary>Edit the existing review of the bot account.</summary>
public sealed class OwnReviewEditRequest {
	[JsonInclude]
	[Description("true = recommended, false = not recommended (required)")]
	public bool? Recommended { get; init; }

	[JsonInclude]
	[Description("Review text, 1-8000 characters")]
	public string? Text { get; init; }

	[JsonInclude]
	[Description("Steam clears the ratings other users gave when the recommendation changes; the caller must acknowledge it")]
	public bool ConfirmRatingReset { get; init; }
}

/// <summary>Outcome of a publish / edit that Steam accepted.</summary>
public sealed class OwnReviewWriteResponse {
	[JsonInclude]
	[Description("published | edited")]
	public string Operation { get; init; } = "";

	[JsonInclude]
	public uint AppId { get; init; }

	[JsonInclude]
	[Description("True when the review was read back from Steam and matched what was sent; false when Steam accepted it but the read-back could not confirm it yet")]
	public bool Verified { get; init; }

	[JsonInclude]
	[Description("The review as read back from Steam; null when the read-back failed")]
	public OwnReviewDetailResponse? Review { get; init; }
}