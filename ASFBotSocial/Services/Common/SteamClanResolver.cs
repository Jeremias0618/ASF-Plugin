using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace ASFBotSocial.Services;

/// <summary>
/// Resolves Steam community group URLs / vanity / clan SteamID64 for JoinGroup.
/// </summary>
internal static class SteamClanResolver {
	private static readonly Regex GroupId64XmlRegex = new(
		@"<groupID64>(\d{17,20})</groupID64>",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	private static readonly Regex GroupNameXmlRegex = new(
		@"<groupName><!\[CDATA\[(.*?)\]\]></groupName>",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline
	);

	private static readonly Regex GroupUrlRegex = new(
		@"steamcommunity\.com/(?:groups|gid)/([^/?#\s]+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
	);

	public static async Task<(ulong ClanId, string? Name, string? Error)> ResolveAsync(Bot bot, string target) {
		ArgumentNullException.ThrowIfNull(bot);

		if (string.IsNullOrWhiteSpace(target)) {
			return (0, null, "Invalid group target");
		}

		string trimmed = target.Trim();

		if (TryParseClanId(trimmed, out ulong directClan)) {
			return (directClan, null, null);
		}

		Match urlMatch = GroupUrlRegex.Match(trimmed);

		if (urlMatch.Success) {
			string segment = SanitizePathSegment(urlMatch.Groups[1].Value);

			if (TryParseClanId(segment, out ulong fromGid)) {
				return (fromGid, null, null);
			}

			return await ResolveVanityAsync(bot, segment).ConfigureAwait(false);
		}

		string vanity = SanitizePathSegment(trimmed);

		if (string.IsNullOrWhiteSpace(vanity)) {
			return (0, null, "Invalid group target");
		}

		if (TryParseClanId(vanity, out ulong asClan)) {
			return (asClan, null, null);
		}

		return await ResolveVanityAsync(bot, vanity).ConfigureAwait(false);
	}

	private static async Task<(ulong ClanId, string? Name, string? Error)> ResolveVanityAsync(Bot bot, string vanity) {
		Uri xmlUri = new($"https://steamcommunity.com/groups/{Uri.EscapeDataString(vanity)}/memberslistxml/?xml=1");

		try {
			BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(xmlUri).ConfigureAwait(false);

			if ((response?.Content == null) || (response.Content.Count == 0)) {
				return (0, null, "Could not resolve group URL");
			}

			byte[] bytes = new byte[response.Content.Count];
			int i = 0;

			foreach (byte b in response.Content) {
				bytes[i++] = b;
			}

			string xml = Encoding.UTF8.GetString(bytes);
			Match match = GroupId64XmlRegex.Match(xml);

			if (!match.Success) {
				return (0, null, "Group not found");
			}

			if (!ulong.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong clanId) || (clanId == 0)) {
				return (0, null, "Invalid groupID64");
			}

			// memberslistxml groupID64 is authoritative — do not require SteamKit AccountType checks
			// (SteamKit/ASF SteamID helpers can disagree across versions).

			string? name = null;
			Match nameMatch = GroupNameXmlRegex.Match(xml);

			if (nameMatch.Success) {
				name = nameMatch.Groups[1].Value.Trim();
			}

			return (clanId, string.IsNullOrEmpty(name) ? null : name, null);
		} catch (Exception e) {
			return (0, null, e.GetType().Name + ": " + e.Message);
		}
	}

	private static bool TryParseClanId(string value, out ulong clanId) {
		clanId = 0;

		if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed) || (parsed == 0)) {
			return false;
		}

		// Clan SteamID64 values from Steam community typically start with this prefix.
		string text = parsed.ToString(CultureInfo.InvariantCulture);

		if ((text.Length is >= 17 and <= 20) && text.StartsWith("10358279", StringComparison.Ordinal)) {
			clanId = parsed;

			return true;
		}

		try {
			SteamID sid = new(parsed);

			if (sid.AccountType == EAccountType.Clan) {
				clanId = parsed;

				return true;
			}
		} catch {
			// Fall through.
		}

		return false;
	}

	private static string SanitizePathSegment(string value) {
		string segment = value.Split('/')[0];
		int query = segment.IndexOf('?');

		if (query >= 0) {
			segment = segment.Substring(0, query);
		}

		int hash = segment.IndexOf('#');

		if (hash >= 0) {
			segment = segment.Substring(0, hash);
		}

		return segment.Trim();
	}
}
