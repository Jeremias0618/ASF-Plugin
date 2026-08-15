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
/// Resolves SteamID64 / friend-code / profile URL / vanity without AngleSharp
/// (plugin host may not ship that assembly next to ASFBotSocial.dll).
/// </summary>
internal static partial class SteamIdResolver {
	[GeneratedRegex(@"<steamID64>(\d{17})</steamID64>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex SteamId64XmlRegex();

	public static async Task<(ulong SteamId, string? Error)> ResolveAsync(Bot bot, string target) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentException.ThrowIfNullOrEmpty(target);

		string trimmed = target.Trim();

		if (TryParseSteamId64OrAccountId(trimmed, out ulong directId)) {
			return (directId, null);
		}

		if (trimmed.Contains("steamcommunity.com/profiles/", StringComparison.OrdinalIgnoreCase)) {
			string idPart = trimmed.Split(["/profiles/"], StringSplitOptions.None)[^1].Trim('/');
			string numeric = SanitizePathSegment(idPart);

			if (TryParseSteamId64OrAccountId(numeric, out ulong fromUrl)) {
				return (fromUrl, null);
			}

			return (0, "Invalid profile URL");
		}

		string vanity = trimmed;

		if (trimmed.Contains("steamcommunity.com/id/", StringComparison.OrdinalIgnoreCase)) {
			vanity = trimmed.Split(["/id/"], StringSplitOptions.None)[^1].Trim('/');
		}

		vanity = SanitizePathSegment(vanity);

		if (string.IsNullOrWhiteSpace(vanity)) {
			return (0, "Invalid vanity URL");
		}

		Uri xmlUri = new($"https://steamcommunity.com/id/{Uri.EscapeDataString(vanity)}/?xml=1");

		try {
			BinaryResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToBinary(xmlUri).ConfigureAwait(false);

			if (response?.Content == null || response.Content.Count == 0) {
				return (0, "Could not resolve vanity URL");
			}

			byte[] bytes = [.. response.Content];
			string xml = Encoding.UTF8.GetString(bytes);
			Match match = SteamId64XmlRegex().Match(xml);

			if (match.Success && TryParseSteamId64OrAccountId(match.Groups[1].Value, out ulong resolved)) {
				return (resolved, null);
			}

			return (0, "Vanity profile not found");
		} catch (Exception e) {
			return (0, e.Message);
		}
	}

	private static bool TryParseSteamId64OrAccountId(string value, out ulong steamId) {
		steamId = 0;

		if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)) {
			return false;
		}

		SteamID sid = new(parsed);

		if (sid.IsIndividualAccount) {
			steamId = parsed;

			return true;
		}

		if (parsed is > 0 and < uint.MaxValue) {
			SteamID fromAccount = new((uint) parsed, EUniverse.Public, EAccountType.Individual);
			steamId = fromAccount;

			return true;
		}

		return false;
	}

	private static string SanitizePathSegment(string value) {
		string segment = value.Split('/')[0];
		int query = segment.IndexOfAny(['?', '#']);

		if (query >= 0) {
			segment = segment[..query];
		}

		return segment.Trim();
	}
}
