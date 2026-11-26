using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ASFBotSocial.Models;

namespace ASFBotSocial.Services;

/// <summary>
/// Reads the public Steam community review pages (<c>/profiles/{id}/recommended/</c>).
/// Steam exposes no JSON API for an author's reviews, so this depends on the web markup;
/// any field that is not found stays <c>null</c> (never guessed).
/// </summary>
internal static class OwnReviewsParser {
	internal const int ExcerptMaxLength = 600;

	private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline;

	private static readonly Regex ReviewBoxRegex = new(@"<div[^>]*class=""review_box""[^>]*>", Opts);
	private static readonly Regex ShowingRegex = new(@"Showing\s+(\d+)\s*-\s*(\d+)\s+of\s+([\d,\.]+)\s+entr", Opts);
	private static readonly Regex HeaderRegex = new(@"<div class=""header"">(.*?)<div style=""clear", Opts);
	private static readonly Regex CapsuleLinkRegex = new(@"class=""game_capsule_ctn[^""]*""", Opts);
	private static readonly Regex AppHrefRegex = new(@"href=""[^""]*?/app/(\d+)", Opts);
	private static readonly Regex ReviewUrlAppRegex = new(@"/recommended/(\d+)", Opts);
	private static readonly Regex CoverRegex = new(@"class=""game_capsule""\s*src=""(https://[^""]+)""", Opts);
	private static readonly Regex TitleLinkRegex = new(@"<div class=""title""[^>]*>(.*?)</div>", Opts);
	private static readonly Regex HoursRegex = new(@"<div class=""hours"">(.*?)</div>", Opts);
	private static readonly Regex ContentRegex = new(@"<div class=""content[^""]*"">(.*?)<div class=""posted"">", Opts);
	private static readonly Regex PostedRegex = new(@"<div class=""posted"">(.*?)</div>", Opts);
	private static readonly Regex ReviewIdRegex = new(@"(?:RecommendationVote(?:Up|Down)Btn|UserReviewVote(?:Up|Down)\([^)]*?,\s*'|UserReview_Report\(\s*'|UserReview_Update\(\s*')(\d{5,20})", Opts);
	private static readonly Regex HelpfulRegex = new(@"([\d,\.]+)\s+(?:person|people)\s+found\s+this\s+review\s+helpful", Opts);
	private static readonly Regex FunnyRegex = new(@"([\d,\.]+)\s+(?:person|people)\s+found\s+this\s+review\s+funny", Opts);
	private static readonly Regex NobodyRatedRegex = new(@"No one has rated this review as helpful", Opts);
	private static readonly Regex AwardBlockRegex = new(@"<div[^>]*class=""review_award tooltip[^""]*""(.*?)<span class=""review_award_count[^>]*>\s*([\d,\.]*)\s*</span>", Opts);
	private static readonly Regex AwardIdRegex = new(@"OnUserReviewAward\s*,\s*(\d+)", Opts);
	private static readonly Regex AwardTooltipRegex = new(@"data-tooltip-html=""(.*?)""", Opts);
	private static readonly Regex AwardNameRegex = new(@"reaction_award_name"">(.*?)</div>", Opts);
	private static readonly Regex AwardIconRegex = new(@"class=""review_award_icon""\s*src=""(https://[^""]+)""", Opts);
	private static readonly Regex TagRegex = new(@"<[^>]+>", Opts);
	private static readonly Regex BreakRegex = new(@"<br\s*/?>", Opts);
	private static readonly Regex BlockEndRegex = new(@"</(?:p|div|li|h\d|blockquote)>", Opts);
	private static readonly Regex ScriptStyleRegex = new(@"<(script|style)[^>]*>.*?</\1>", Opts);
	private static readonly Regex DetailTitleRegex = new(@"<title>[^<]*?::\s*Review for\s+(.*?)</title>", Opts);
	private static readonly Regex DetailRatingRegex = new(@"<div class=""ratingSummary"">(.*?)</div>", Opts);
	private static readonly Regex DetailPlaytimeRegex = new(@"<div class=""playTime"">(.*?)</div>", Opts);
	private static readonly Regex DetailDateRegex = new(@"<div class=""recommendation_date"">(.*?)</div>", Opts);
	private static readonly Regex DetailTextRegex = new(@"<div id=""ReviewText"">(.*?)</div>\s*<div id=""ReviewEdit""", Opts);
	private static readonly Regex DetailRatingBarRegex = new(@"<div class=""ratingBar"">(.*?)<div class=""ratingSummaryBlock""", Opts);
	// Owner-only markup: Steam renders these scripts and the edit form (with the stored raw text) only for the author.
	private static readonly Regex OwnerUpdateIdRegex = new(@"UserReview_Update\(\s*'(\d{5,20})'", Opts);
	private static readonly Regex EditTextAreaRegex = new(@"<textarea[^>]*id=""ReviewEditTextArea""[^>]*>(.*?)</textarea>", Opts);
	private static readonly Regex VotedUpOriginalRegex = new(@"var\s+voted_up_og\s*=\s*(true|false)\s*;", Opts);
	private static readonly Regex ReceivedCompensationInputRegex = new(@"<input[^>]*id=""ReceivedCompensationCheckbox""[^>]*>", Opts);
	private static readonly Regex HardwareInputRegex = new(@"<input[^>]*id=""AttachedPCHardware""[^>]*>", Opts);
	private static readonly Regex ValueAttributeRegex = new(@"\bvalue=""(\d*)""", Opts);
	private static readonly Regex WhitespaceRegex = new(@"[ \t\u00A0]+", Opts);
	private static readonly Regex BlankLinesRegex = new(@"(?:[ \t]*\r?\n){3,}", Opts);

	private static readonly string[] DateFormats = ["d MMMM, yyyy", "d MMMM yyyy", "MMMM d, yyyy", "d MMM, yyyy", "d MMM yyyy", "MMM d, yyyy"];

	internal sealed class ListPage {
		internal uint? Total { get; init; }
		internal uint? RangeStart { get; init; }
		internal uint? RangeEnd { get; init; }
		internal List<OwnReviewEntry> Items { get; init; } = [];
		internal bool IsErrorPage { get; init; }
	}

	internal static ListPage ParseList(string html, ulong steamId) {
		ArgumentNullException.ThrowIfNull(html);

		bool isError = html.Contains("<title>Steam Community :: Error</title>", StringComparison.OrdinalIgnoreCase);
		uint? total = null;
		uint? start = null;
		uint? end = null;
		Match showing = ShowingRegex.Match(html);

		if (showing.Success) {
			start = ParseUInt(showing.Groups[1].Value);
			end = ParseUInt(showing.Groups[2].Value);
			total = ParseUInt(showing.Groups[3].Value);
		}

		MatchCollection boxes = ReviewBoxRegex.Matches(html);
		List<OwnReviewEntry> items = new(boxes.Count);

		for (int i = 0; i < boxes.Count; i++) {
			int from = boxes[i].Index;
			int to = i + 1 < boxes.Count ? boxes[i + 1].Index : html.Length;
			OwnReviewEntry? entry = ParseBox(html.Substring(from, to - from), steamId);

			if (entry != null) {
				items.Add(entry);
			}
		}

		return new ListPage { Total = total, RangeStart = start, RangeEnd = end, Items = items, IsErrorPage = isError };
	}

	private static OwnReviewEntry? ParseBox(string box, ulong steamId) {
		uint appId = 0;
		Match capsule = CapsuleLinkRegex.Match(box);

		if (capsule.Success) {
			int hrefStart = box.LastIndexOf("<a ", capsule.Index, StringComparison.OrdinalIgnoreCase);

			if (hrefStart >= 0) {
				Match app = AppHrefRegex.Match(box, hrefStart, Math.Min(400, box.Length - hrefStart));

				if (app.Success) {
					appId = ParseUInt(app.Groups[1].Value) ?? 0;
				}
			}
		}

		if (appId == 0) {
			Match fromUrl = ReviewUrlAppRegex.Match(box);

			if (fromUrl.Success) {
				appId = ParseUInt(fromUrl.Groups[1].Value) ?? 0;
			}
		}

		if (appId == 0) {
			return null;
		}

		string? recommendation = GroupText(TitleLinkRegex, box);
		string content = ToPlainText(ContentRegex.Match(box).Groups[1].Value);
		bool truncated = false;

		if (content.Length > ExcerptMaxLength) {
			content = content[..ExcerptMaxLength].TrimEnd() + "…";
			truncated = true;
		}

		string? posted = Clean(GroupText(PostedRegex, box));
		Match header = HeaderRegex.Match(box);
		(uint? helpful, uint? funny) = ParseCounts(header.Success ? header.Groups[1].Value : "");
		Match idMatch = ReviewIdRegex.Match(box);
		string? cover = CoverRegex.Match(box) is { Success: true } c ? c.Groups[1].Value : null;

		return new OwnReviewEntry {
			AppId = appId,
			ReviewId = idMatch.Success ? idMatch.Groups[1].Value : null,
			Url = ReviewUrl(steamId, appId),
			Recommended = ParseRecommendation(recommendation),
			Excerpt = content,
			ExcerptTruncated = truncated,
			PostedText = posted,
			PostedDate = ParsePostedDate(posted),
			PlaytimeText = Clean(GroupText(HoursRegex, box)),
			CoverUrl = cover,
			HelpfulCount = helpful,
			FunnyCount = funny,
			Awards = ParseAwards(header.Success ? header.Groups[1].Value : ""),
		};
	}

	internal static OwnReviewDetailResponse? ParseDetail(string html, ulong steamId, uint appId) {
		ArgumentNullException.ThrowIfNull(html);

		Match text = DetailTextRegex.Match(html);

		if (!text.Success) {
			return null;
		}

		Match bar = DetailRatingBarRegex.Match(html);
		string barHtml = bar.Success ? bar.Groups[1].Value : "";
		(uint? helpful, uint? funny) = ParseCounts(barHtml);
		Match idMatch = ReviewIdRegex.Match(html);
		string? title = Clean(DetailTitleRegex.Match(html) is { Success: true } t ? t.Groups[1].Value : null);
		string? posted = Clean(GroupText(DetailDateRegex, html));

		if (posted != null) {
			posted = posted.StartsWith("Posted:", StringComparison.OrdinalIgnoreCase) ? posted[7..].Trim() : posted;
		}

		return new OwnReviewDetailResponse {
			SteamId = steamId.ToString(CultureInfo.InvariantCulture),
			AppId = appId,
			ReviewId = idMatch.Success ? idMatch.Groups[1].Value : null,
			Url = ReviewUrl(steamId, appId),
			Title = title,
			Recommended = ParseRecommendation(GroupText(DetailRatingRegex, html)),
			Text = ToPlainText(text.Groups[1].Value),
			PostedText = posted,
			PlaytimeText = Clean(GroupText(DetailPlaytimeRegex, html)),
			HelpfulCount = helpful,
			FunnyCount = funny,
			Awards = ParseAwards(barHtml),
		};
	}

	internal sealed class EditorState {
		internal required string ReviewId { get; init; }
		internal required bool Recommended { get; init; }
		internal required string Text { get; init; }
		internal bool ReceivedCompensation { get; init; }

		/// <summary>"0" when no PC hardware is attached, otherwise the id Steam's own form would send back.</summary>
		internal string SavedHardwareId { get; init; } = "0";
	}

	/// <summary>
	/// Reads the author's edit form from the review page. Returns null unless the page was rendered for the author
	/// (Steam emits the <c>UserReview_Update</c> scripts only for the owner), so it doubles as an ownership check.
	/// </summary>
	internal static EditorState? ParseEditor(string html) {
		ArgumentNullException.ThrowIfNull(html);

		Match id = OwnerUpdateIdRegex.Match(html);
		Match area = EditTextAreaRegex.Match(html);
		Match votedUp = VotedUpOriginalRegex.Match(html);

		if (!id.Success || !area.Success || !votedUp.Success) {
			return null;
		}

		string hardware = "0";
		Match hardwareInput = HardwareInputRegex.Match(html);

		if (hardwareInput.Success && hardwareInput.Value.Contains("checked", StringComparison.OrdinalIgnoreCase)) {
			Match value = ValueAttributeRegex.Match(hardwareInput.Value);
			hardware = value.Success && value.Groups[1].Value.Length > 0 ? value.Groups[1].Value : "0";
		}

		return new EditorState {
			ReviewId = id.Groups[1].Value,
			Recommended = votedUp.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase),
			Text = NormalizeNewlines(WebUtility.HtmlDecode(area.Groups[1].Value)),
			ReceivedCompensation = ReceivedCompensationInputRegex.Match(html) is { Success: true } rc && rc.Value.Contains("checked", StringComparison.OrdinalIgnoreCase),
			SavedHardwareId = hardware,
		};
	}

	internal static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

	internal static string ReviewUrl(ulong steamId, uint appId) =>
		string.Create(CultureInfo.InvariantCulture, $"https://steamcommunity.com/profiles/{steamId}/recommended/{appId}/");

	private static (uint? Helpful, uint? Funny) ParseCounts(string headerHtml) {
		string text = ToPlainText(Regex.Replace(headerHtml, @"<div class=""review_award_ctn"".*", "", Opts));
		uint? helpful = null;
		uint? funny = null;
		Match h = HelpfulRegex.Match(text);

		if (h.Success) {
			helpful = ParseUInt(h.Groups[1].Value);
		} else if (NobodyRatedRegex.IsMatch(text)) {
			// Steam explicitly says nobody rated it: a real zero, not a guess.
			helpful = 0;
		}

		Match f = FunnyRegex.Match(text);

		if (f.Success) {
			funny = ParseUInt(f.Groups[1].Value);
		}

		return (helpful, funny);
	}

	private static List<OwnReviewAward> ParseAwards(string html) {
		List<OwnReviewAward> awards = [];

		foreach (Match block in AwardBlockRegex.Matches(html)) {
			string body = block.Groups[1].Value;
			Match tooltip = AwardTooltipRegex.Match(body);

			if (!tooltip.Success) {
				continue;
			}

			string decoded = WebUtility.HtmlDecode(tooltip.Groups[1].Value);
			Match name = AwardNameRegex.Match(decoded);

			if (!name.Success) {
				continue;
			}

			Match id = AwardIdRegex.Match(body);
			Match icon = AwardIconRegex.Match(body);

			awards.Add(new OwnReviewAward {
				Id = id.Success ? ParseUInt(id.Groups[1].Value) : null,
				Name = Clean(WebUtility.HtmlDecode(name.Groups[1].Value)) ?? "",
				IconUrl = icon.Success ? icon.Groups[1].Value : null,
				Count = ParseUInt(block.Groups[2].Value),
			});
		}

		return awards;
	}

	private static bool? ParseRecommendation(string? raw) {
		string? text = Clean(raw is null ? null : ToPlainText(raw));

		if (string.IsNullOrEmpty(text)) {
			return null;
		}

		if (text.Contains("Not Recommended", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		return text.Contains("Recommended", StringComparison.OrdinalIgnoreCase) ? true : null;
	}

	internal static string? ParsePostedDate(string? posted) {
		if (string.IsNullOrWhiteSpace(posted)) {
			return null;
		}

		string candidate = posted.Trim();

		if (candidate.StartsWith("Posted", StringComparison.OrdinalIgnoreCase)) {
			candidate = candidate[6..].TrimStart(':', ' ');
		}

		int dot = candidate.IndexOf('.', StringComparison.Ordinal);

		if (dot > 0) {
			candidate = candidate[..dot];
		}

		int at = candidate.IndexOf('@', StringComparison.Ordinal);

		if (at > 0) {
			candidate = candidate[..at];
		}

		candidate = candidate.Trim();

		// "7 November, 2022" or (current year) "7 November" is not parseable without a year: leave it null.
		return DateTime.TryParseExact(candidate, DateFormats, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out DateTime date)
			? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			: null;
	}

	private static string? GroupText(Regex regex, string source) {
		Match match = regex.Match(source);

		return match.Success ? ToPlainText(match.Groups[1].Value) : null;
	}

	private static string? Clean(string? value) {
		if (value == null) {
			return null;
		}

		string cleaned = WhitespaceRegex.Replace(WebUtility.HtmlDecode(value), " ").Trim();

		return cleaned.Length == 0 ? null : cleaned;
	}

	/// <summary>Strips every tag/script and decodes entities; the result is plain text (never HTML).</summary>
	internal static string ToPlainText(string html) {
		if (string.IsNullOrEmpty(html)) {
			return "";
		}

		string withoutScripts = ScriptStyleRegex.Replace(html, "");
		string withBreaks = BlockEndRegex.Replace(BreakRegex.Replace(withoutScripts, "\n"), "\n");
		string noTags = TagRegex.Replace(withBreaks, "");
		string decoded = WebUtility.HtmlDecode(noTags).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		StringBuilder builder = new(decoded.Length);

		foreach (string line in decoded.Split('\n')) {
			builder.Append(WhitespaceRegex.Replace(line, " ").Trim()).Append('\n');
		}

		return BlankLinesRegex.Replace(builder.ToString(), "\n\n").Trim();
	}

	private static uint? ParseUInt(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return null;
		}

		string digits = value.Replace(",", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).Trim();

		return uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed) ? parsed : null;
	}
}
