using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IpcConfig;

public sealed class IpcConfigWriteRequest {
	[JsonInclude]
	public bool ListenLan { get; private init; }

	[JsonInclude]
	public ushort Port { get; private init; } = 1242;

	[JsonInclude]
	public string PathBase { get; private init; } = "/";

	[JsonInclude]
	public IReadOnlyList<string>? KnownNetworks { get; private init; }

	[JsonConstructor]
	private IpcConfigWriteRequest() { }
}

public sealed class IpcConfigStatusResponse {
	private static readonly Regex PortRegex = new(@":(\d+)(?:/|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

	[JsonInclude]
	public bool FileExists { get; private init; }

	[JsonInclude]
	public string Path { get; private init; } = "";

	[JsonInclude]
	public bool ListenLan { get; private init; }

	[JsonInclude]
	public ushort Port { get; private init; } = 1242;

	[JsonInclude]
	public string PathBase { get; private init; } = "/";

	[JsonInclude]
	public IReadOnlyList<string> KnownNetworks { get; private init; } = [];

	/// <summary>Raw IPC.config JSON text (avoid JsonNode on public API — TypeLoadException under ASF IPC).</summary>
	[JsonInclude]
	public string? RawJson { get; private init; }

	[JsonInclude]
	public bool RestartRequired { get; set; }

	internal static IpcConfigStatusResponse FromDefaults(string path, bool fileExists) =>
		new() {
			FileExists = fileExists,
			Path = path,
			ListenLan = false,
			Port = 1242,
			PathBase = "/",
			KnownNetworks = [],
			RawJson = null,
			RestartRequired = false
		};

	internal static IpcConfigStatusResponse FromDocument(string path, bool fileExists, JsonNode? root) {
		JsonNode? kestrel = root?["Kestrel"];
		string? url = kestrel?["Endpoints"]?["HTTP"]?["Url"]?.GetValue<string>()
			?? kestrel?["Endpoints"]?.AsObject().FirstOrDefault().Value?["Url"]?.GetValue<string>();

		bool listenLan = url?.Contains('*', System.StringComparison.Ordinal) == true
			|| url?.Contains("0.0.0.0", System.StringComparison.Ordinal) == true;

		ushort port = 1242;

		if (!string.IsNullOrEmpty(url)) {
			Match match = PortRegex.Match(url);

			if (match.Success && ushort.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort parsed)) {
				port = parsed;
			}
		}

		string pathBase = kestrel?["PathBase"]?.GetValue<string>() ?? "/";
		List<string> networks = [];

		if (kestrel?["KnownNetworks"] is JsonArray array) {
			foreach (JsonNode? node in array) {
				string? cidr = node?.GetValue<string>();

				if (!string.IsNullOrWhiteSpace(cidr)) {
					networks.Add(cidr);
				}
			}
		}

		return new IpcConfigStatusResponse {
			FileExists = fileExists,
			Path = path,
			ListenLan = listenLan,
			Port = port,
			PathBase = pathBase,
			KnownNetworks = networks,
			RawJson = root?.ToJsonString(),
			RestartRequired = false
		};
	}
}
