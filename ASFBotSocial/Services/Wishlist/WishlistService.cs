using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using ASFBotSocial.Models;

namespace ASFBotSocial.Services;

internal sealed class WishlistService {
	private const int NameBatchSize = 50;
	private readonly RateLimiter rateLimiter = new(TimeSpan.FromSeconds(2));

	public async Task<WishlistResponse?> ListAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		List<WishlistEntry> items;

		try {
			items = await FetchWishlistItemsAsync(bot).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		if (items.Count == 0) {
			return new WishlistResponse {
				Total = 0,
				Items = Array.Empty<WishlistEntry>(),
			};
		}

		Dictionary<uint, string> names;

		try {
			names = await ResolveNamesAsync(bot, items.Select(static item => item.AppId).ToList()).ConfigureAwait(false);
		} catch (Exception e) {
			// Names are optional; AppID placeholders still make the list usable.
			bot.ArchiLogger.LogGenericWarningException(e);
			names = new Dictionary<uint, string>();
		}

		List<WishlistEntry> resolved = new(items.Count);

		foreach (WishlistEntry item in items) {
			string name = item.Name;

			if (names.TryGetValue(item.AppId, out string? resolvedName) && !string.IsNullOrWhiteSpace(resolvedName)) {
				name = resolvedName;
			}

			resolved.Add(
				new WishlistEntry {
					AppId = item.AppId,
					Name = name,
					Priority = item.Priority,
				}
			);
		}

		resolved.Sort(
			static (a, b) => {
				int byPriority = (a.Priority ?? uint.MaxValue).CompareTo(b.Priority ?? uint.MaxValue);

				return byPriority != 0
					? byPriority
					: string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
			}
		);

		return new WishlistResponse {
			Total = resolved.Count,
			Items = resolved,
		};
	}

	/// <summary>Follow a store app only (idempotent).</summary>
	public async Task<MutationsResponse> FollowGameOnlyAsync(Bot bot, string urlOrAppId, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		string trimmed = (urlOrAppId ?? "").Trim();

		if (string.IsNullOrEmpty(trimmed)) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = "", Message = "Empty URL" }],
			};
		}

		if (!TryParseAppId(trimmed, out uint appId)) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = trimmed, Message = "Invalid store URL or AppID" }],
			};
		}

		string target = appId.ToString(CultureInfo.InvariantCulture);

		if (!bot.IsConnectedAndLoggedOn) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = target, Message = "Bot is not connected" }],
			};
		}

		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			(_, HashSet<uint> followed) = await LoadWishlistAndFollowedAsync(bot).ConfigureAwait(false);

			if (followed.Contains(appId)) {
				return new MutationsResponse {
					Results = [new MutationResult { Success = true, Target = target, Message = "follow: already" }],
				};
			}

			bool followedOk = await FollowGameAsync(bot, appId).ConfigureAwait(false);

			return new MutationsResponse {
				Results = [
					new MutationResult {
						Success = followedOk,
						Target = target,
						Message = followedOk ? "follow: added" : "follow: failed",
					},
				],
			};
		} catch (Exception e) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = target, Message = e.Message }],
			};
		}
	}

	public async Task<MutationsResponse> FollowAndWishlistAsync(Bot bot, string urlOrAppId, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);

		string trimmed = (urlOrAppId ?? "").Trim();

		if (string.IsNullOrEmpty(trimmed)) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = "", Message = "Empty URL" }],
			};
		}

		if (!TryParseAppId(trimmed, out uint appId)) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = trimmed, Message = "Invalid store URL or AppID" }],
			};
		}

		string target = appId.ToString(CultureInfo.InvariantCulture);

		if (!bot.IsConnectedAndLoggedOn) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = target, Message = "Bot is not connected" }],
			};
		}

		await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

		try {
			(HashSet<uint> wishlisted, HashSet<uint> followed) = await LoadWishlistAndFollowedAsync(bot).ConfigureAwait(false);

			List<string> notes = [];
			bool ok = true;

			if (wishlisted.Contains(appId)) {
				notes.Add("wishlist: already");
			} else {
				await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);
				bool added = await AddWishlistOneAsync(bot, appId).ConfigureAwait(false);

				if (added) {
					notes.Add("wishlist: added");
				} else {
					notes.Add("wishlist: failed");
					ok = false;
				}
			}

			if (followed.Contains(appId)) {
				notes.Add("follow: already");
			} else {
				await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);
				bool followedOk = await FollowGameAsync(bot, appId).ConfigureAwait(false);

				if (followedOk) {
					notes.Add("follow: added");
				} else {
					notes.Add("follow: failed");
					ok = false;
				}
			}

			return new MutationsResponse {
				Results = [
					new MutationResult {
						Success = ok,
						Target = target,
						Message = string.Join("; ", notes),
					},
				],
			};
		} catch (Exception e) {
			return new MutationsResponse {
				Results = [new MutationResult { Success = false, Target = target, Message = e.Message }],
			};
		}
	}

	internal static bool TryParseAppId(string value, out uint appId) {
		appId = 0;

		if (string.IsNullOrWhiteSpace(value)) {
			return false;
		}

		string trimmed = value.Trim();

		if (TryParseAppIdAfterMarker(trimmed, "/app/", out appId)) {
			return true;
		}

		if (TryParseAppIdAfterMarker(trimmed, "steam://store/", out appId)) {
			return true;
		}

		if (TryParseAppIdAfterMarker(trimmed, "steam://run/", out appId)) {
			return true;
		}

		return uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId) && (appId > 0);
	}

	private static bool TryParseAppIdAfterMarker(string value, string marker, out uint appId) {
		appId = 0;
		int markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

		if (markerIndex < 0) {
			return false;
		}

		int start = markerIndex + marker.Length;
		int end = start;

		while ((end < value.Length) && (value[end] >= '0') && (value[end] <= '9')) {
			end++;
		}

		if (end <= start) {
			return false;
		}

		return uint.TryParse(value.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out appId) && (appId > 0);
	}

	private static async Task<(HashSet<uint> Wishlisted, HashSet<uint> Followed)> LoadWishlistAndFollowedAsync(Bot bot) {
		HashSet<uint> wishlisted = [];
		HashSet<uint> followed = [];

		try {
			List<WishlistEntry> items = await FetchWishlistItemsAsync(bot).ConfigureAwait(false);

			foreach (WishlistEntry item in items) {
				if (item.AppId > 0) {
					wishlisted.Add(item.AppId);
				}
			}
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Wishlist check failed: " + e.Message);
		}

		try {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/dynamicstore/userdata/");
			ObjectResponse<JsonNode>? response = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<JsonNode>(
				request,
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
				maxTries: 2
			).ConfigureAwait(false);

			JsonArray? followedNode = response?.Content?["rgFollowedApps"] as JsonArray;

			if (followedNode != null) {
				foreach (JsonNode? node in followedNode) {
					uint id = ReadUInt(node);

					if (id > 0) {
						followed.Add(id);
					}
				}
			}

			// Prefer IWishlistService when available; fall back to dynamicstore wishlist snapshot.
			if (wishlisted.Count == 0) {
				JsonArray? wishNode = response?.Content?["rgWishlist"] as JsonArray;

				if (wishNode != null) {
					foreach (JsonNode? node in wishNode) {
						uint id = ReadUInt(node);

						if (id > 0) {
							wishlisted.Add(id);
						}
					}
				}
			}
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarning("Followed apps check failed: " + e.Message);
		}

		return (wishlisted, followed);
	}

	private static async Task<bool> AddWishlistOneAsync(Bot bot, uint appId) {
		Uri request = new(ArchiWebHandler.SteamStoreURL, "/api/addtowishlist");
		Dictionary<string, string> data = new(1, StringComparer.Ordinal) {
			["appid"] = appId.ToString(CultureInfo.InvariantCulture),
		};

		return await PostSuccessAsync(bot, request, data).ConfigureAwait(false);
	}

	private static async Task<bool> FollowGameAsync(Bot bot, uint appId) {
		Uri request = new(ArchiWebHandler.SteamStoreURL, "/explore/followgame/");
		Dictionary<string, string> data = new(1, StringComparer.Ordinal) {
			["appid"] = appId.ToString(CultureInfo.InvariantCulture),
		};

		try {
			ObjectResponse<JsonNode>? json = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<JsonNode>(
				request,
				data: data,
				referer: new Uri(ArchiWebHandler.SteamStoreURL, $"/app/{appId}/"),
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
				maxTries: 2
			).ConfigureAwait(false);

			if (IsTruthy(json?.Content) || IsTruthy(json?.Content?["success"])) {
				return true;
			}

			string? raw = json?.Content?.ToString()?.Trim();

			if (!string.IsNullOrEmpty(raw)
				&& raw.Equals("true", StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericDebug("FollowGame JSON parse note: " + e.Message);
		}

		// Fallback: Steam sometimes returns plain text; treat HTTP OK as success.
		try {
			return await bot.ArchiWebHandler.UrlPostWithSession(
				request,
				data: data,
				referer: new Uri(ArchiWebHandler.SteamStoreURL, $"/app/{appId}/"),
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors,
				maxTries: 2
			).ConfigureAwait(false);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return false;
		}
	}

	public async Task<MutationsResponse> AddAsync(Bot bot, IReadOnlyCollection<uint> appIds, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(appIds);

		List<MutationResult> results = new();

		foreach (uint appId in appIds.Distinct()) {
			await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			if (appId == 0) {
				results.Add(new MutationResult { Success = false, Target = "0", Message = "Invalid AppID" });

				continue;
			}

			Uri request = new(ArchiWebHandler.SteamStoreURL, "/api/addtowishlist");
			Dictionary<string, string> data = new(1, StringComparer.Ordinal) {
				["appid"] = appId.ToString(CultureInfo.InvariantCulture),
			};

			bool ok = await PostSuccessAsync(bot, request, data).ConfigureAwait(false);

			results.Add(
				new MutationResult {
					Success = ok,
					Target = appId.ToString(CultureInfo.InvariantCulture),
					Message = ok ? "OK" : "AddWishlist failed",
				}
			);
		}

		return new MutationsResponse { Results = results };
	}

	public async Task<MutationsResponse> RemoveAsync(Bot bot, IReadOnlyCollection<uint> appIds, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(appIds);

		List<MutationResult> results = new();

		foreach (uint appId in appIds.Distinct()) {
			await rateLimiter.WaitAsync(bot.BotName, cancellationToken).ConfigureAwait(false);

			if (appId == 0) {
				results.Add(new MutationResult { Success = false, Target = "0", Message = "Invalid AppID" });

				continue;
			}

			Uri request = new(ArchiWebHandler.SteamStoreURL, "/api/removefromwishlist");
			Dictionary<string, string> data = new(1, StringComparer.Ordinal) {
				["appid"] = appId.ToString(CultureInfo.InvariantCulture),
			};

			bool ok = await PostSuccessAsync(bot, request, data).ConfigureAwait(false);

			results.Add(
				new MutationResult {
					Success = ok,
					Target = appId.ToString(CultureInfo.InvariantCulture),
					Message = ok ? "OK" : "RemoveWishlist failed",
				}
			);
		}

		return new MutationsResponse { Results = results };
	}

	/// <summary>
	/// Old store wishlistdata endpoints return HTML since late 2024.
	/// Use IWishlistService/GetWishlist (one request, no paging).
	/// </summary>
	private static async Task<List<WishlistEntry>> FetchWishlistItemsAsync(Bot bot) {
		StringBuilder query = new();
		query.Append("steamid=");
		query.Append(bot.SteamID.ToString(CultureInfo.InvariantCulture));

		string? accessToken = bot.AccessToken;

		if (!string.IsNullOrEmpty(accessToken)) {
			query.Append("&access_token=");
			query.Append(Uri.EscapeDataString(accessToken));
		}

		Uri request = new(bot.SteamConfiguration.WebAPIBaseAddress, "/IWishlistService/GetWishlist/v1/?" + query);

		ObjectResponse<JsonNode>? response = await ArchiWebHandler.WebLimitRequest(
			bot.SteamConfiguration.WebAPIBaseAddress,
			async () => await bot.ArchiWebHandler.WebBrowser.UrlGetToJsonObject<JsonNode>(
				request,
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
				maxTries: 2
			).ConfigureAwait(false)
		).ConfigureAwait(false);

		if (response == null) {
			throw new InvalidOperationException("Wishlist request failed");
		}

		if ((response.StatusCode == HttpStatusCode.Unauthorized) || (response.StatusCode == HttpStatusCode.Forbidden)) {
			throw new InvalidOperationException("Wishlist is private or access denied");
		}

		int status = (int) response.StatusCode;

		if ((status < 200) || (status > 299)) {
			throw new InvalidOperationException($"Wishlist HTTP {status}");
		}

		JsonNode? itemsNode = response.Content?["response"]?["items"];
		JsonArray? itemsArray = itemsNode as JsonArray;

		if (itemsArray == null) {
			return new List<WishlistEntry>();
		}

		Dictionary<uint, WishlistEntry> unique = new();

		foreach (JsonNode? node in itemsArray) {
			if (node == null) {
				continue;
			}

			uint appId = ReadUInt(node["appid"]);

			if (appId == 0) {
				continue;
			}

			uint priorityValue = ReadUInt(node["priority"]);
			uint? priority = priorityValue > 0 ? priorityValue : null;

			unique[appId] = new WishlistEntry {
				AppId = appId,
				Name = "App " + appId.ToString(CultureInfo.InvariantCulture),
				Priority = priority,
			};
		}

		return unique.Values.ToList();
	}

	private static async Task<Dictionary<uint, string>> ResolveNamesAsync(Bot bot, IReadOnlyList<uint> appIds) {
		Dictionary<uint, string> names = new(appIds.Count);

		if (appIds.Count == 0) {
			return names;
		}

		for (int offset = 0; offset < appIds.Count; offset += NameBatchSize) {
			int count = Math.Min(NameBatchSize, appIds.Count - offset);
			string inputJson = BuildGetItemsInputJson(appIds, offset, count);
			Uri request = new(
				bot.SteamConfiguration.WebAPIBaseAddress,
				"/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(inputJson)
			);

			try {
				ObjectResponse<JsonNode>? response = await ArchiWebHandler.WebLimitRequest(
					bot.SteamConfiguration.WebAPIBaseAddress,
					async () => await bot.ArchiWebHandler.WebBrowser.UrlGetToJsonObject<JsonNode>(
						request,
						requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
						maxTries: 2
					).ConfigureAwait(false)
				).ConfigureAwait(false);

				JsonArray? storeItems = response?.Content?["response"]?["store_items"] as JsonArray;

				if (storeItems == null) {
					continue;
				}

				foreach (JsonNode? item in storeItems) {
					if (item == null) {
						continue;
					}

					uint appId = ReadUInt(item["appid"]);
					string? name = ReadString(item["name"]);

					if ((appId > 0) && !string.IsNullOrWhiteSpace(name)) {
						names[appId] = name;
					}
				}
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericWarningException(e);
			}
		}

		return names;
	}

	private static string BuildGetItemsInputJson(IReadOnlyList<uint> appIds, int offset, int count) {
		StringBuilder sb = new(64 + (count * 24));
		sb.Append("{\"ids\":[");

		for (int i = 0; i < count; i++) {
			if (i > 0) {
				sb.Append(',');
			}

			sb.Append("{\"appid\":");
			sb.Append(appIds[offset + i].ToString(CultureInfo.InvariantCulture));
			sb.Append('}');
		}

		sb.Append("],\"context\":{\"language\":\"english\",\"country_code\":\"US\",\"steam_realm\":1},\"data_request\":{\"include_basic_info\":true}}");

		return sb.ToString();
	}

	private static async Task<bool> PostSuccessAsync(Bot bot, Uri request, Dictionary<string, string> data) {
		try {
			ObjectResponse<JsonNode>? response = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<JsonNode>(
				request,
				data: data,
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
				maxTries: 2
			).ConfigureAwait(false);

			return IsTruthy(response?.Content?["success"]);
		} catch (Exception e) {
			bot.ArchiLogger.LogGenericWarningException(e);

			return false;
		}
	}

	private static uint ReadUInt(JsonNode? node) {
		if (node == null) {
			return 0;
		}

		try {
			JsonValueKind kind = node.GetValueKind();

			if (kind == JsonValueKind.Number) {
				return node.GetValue<uint>();
			}

			if (kind == JsonValueKind.String) {
				return uint.TryParse(node.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed)
					? parsed
					: 0u;
			}
		} catch (Exception) {
			// ignored
		}

		return 0;
	}

	private static string? ReadString(JsonNode? node) {
		if (node == null) {
			return null;
		}

		try {
			if (node.GetValueKind() == JsonValueKind.String) {
				return node.GetValue<string>();
			}

			return node.ToString();
		} catch (Exception) {
			return null;
		}
	}

	private static bool IsTruthy(JsonNode? node) {
		if (node == null) {
			return false;
		}

		try {
			JsonValueKind kind = node.GetValueKind();

			if (kind == JsonValueKind.True) {
				return true;
			}

			if (kind == JsonValueKind.Number) {
				return node.GetValue<int>() != 0;
			}

			if (kind == JsonValueKind.String) {
				string? value = node.GetValue<string>();

				return (value == "1") || (value == "true") || (value == "True");
			}
		} catch (Exception) {
			// ignored
		}

		return false;
	}
}
