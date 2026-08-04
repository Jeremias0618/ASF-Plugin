using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;

namespace IpcConfig;

/// <summary>
/// Reads and writes <c>config/IPC.config</c> using the same JSON shape ASF-ui builds.
/// </summary>
public sealed class IpcConfigFileService {
	/// <summary>Matches SharedInfo.IPCConfigFile (internal in ASF; plugins must use the literal).</summary>
	internal const string IpcConfigFileName = "IPC.config";

	private static readonly JsonSerializerOptions WriteOptions = new() {
		WriteIndented = true
	};

	private readonly SemaphoreSlim FileLock = new(1, 1);

	internal string AbsolutePath => Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.ConfigDirectory, IpcConfigFileName);

	internal async Task<IpcConfigStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default) {
		string path = AbsolutePath;
		bool exists = File.Exists(path);

		if (!exists) {
			return IpcConfigStatusResponse.FromDefaults(path, fileExists: false);
		}

		await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try {
			await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			JsonNode? root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

			return IpcConfigStatusResponse.FromDocument(path, fileExists: true, root);
		} finally {
			FileLock.Release();
		}
	}

	internal async Task<IpcConfigStatusResponse> WriteAsync(IpcConfigWriteRequest request, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		Validate(request);

		JsonObject document = BuildDocument(request);
		string path = AbsolutePath;
		string? directory = Path.GetDirectoryName(path);

		if (string.IsNullOrEmpty(directory)) {
			throw new InvalidOperationException("Unable to resolve IPC.config directory.");
		}

		Directory.CreateDirectory(directory);

		string tempPath = path + ".tmp";
		string json = document.ToJsonString(WriteOptions);

		await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try {
			await File.WriteAllTextAsync(tempPath, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
			File.Move(tempPath, path, overwrite: true);
		} finally {
			FileLock.Release();
		}

		// With ConfigWatch enabled (ASF default), core calls ArchiKestrel.Restart() on IPC.config change.
		ASF.ArchiLogger.LogGenericInfo($"IpcConfig wrote {SharedInfo.ConfigDirectory}/{IpcConfigFileName}. Kestrel reloads via ConfigWatch or after full ASF restart.");

		IpcConfigStatusResponse status = IpcConfigStatusResponse.FromDocument(path, fileExists: true, document);
		status.RestartRequired = true;

		return status;
	}

	internal async Task<bool> DeleteAsync(CancellationToken cancellationToken = default) {
		string path = AbsolutePath;

		await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try {
			if (!File.Exists(path)) {
				return false;
			}

			File.Delete(path);
			ASF.ArchiLogger.LogGenericInfo("IpcConfig deleted IPC.config. ConfigWatch should restart Kestrel; otherwise restart ASF.");

			return true;
		} finally {
			FileLock.Release();
		}
	}

	private static void Validate(IpcConfigWriteRequest request) {
		if (request.Port is < 1 or > 65535) {
			throw new ArgumentOutOfRangeException(nameof(request.Port), request.Port, "Port must be between 1 and 65535.");
		}

		string pathBase = string.IsNullOrWhiteSpace(request.PathBase) ? "/" : request.PathBase.Trim();

		if (!pathBase.StartsWith('/')) {
			throw new ArgumentException("PathBase must start with '/'.", nameof(request.PathBase));
		}

		IReadOnlyList<string> networks = request.KnownNetworks ?? Array.Empty<string>();

		foreach (string cidr in networks) {
			if (string.IsNullOrWhiteSpace(cidr) || !TryParseCidr(cidr.Trim(), out _)) {
				throw new ArgumentException($"Invalid CIDR: {cidr}", nameof(request.KnownNetworks));
			}
		}

		if (request.ListenLan && (ASF.GlobalConfig?.IPCPassword is null or { Length: 0 })) {
			throw new InvalidOperationException("IPCPassword is required when listening on LAN (*). Set it via POST /Api/Asf first.");
		}
	}

	private static JsonObject BuildDocument(IpcConfigWriteRequest request) {
		string host = request.ListenLan ? "*" : "127.0.0.1";
		string url = string.Create(CultureInfo.InvariantCulture, $"http://{host}:{request.Port}");
		string pathBase = string.IsNullOrWhiteSpace(request.PathBase) ? "/" : request.PathBase.Trim();

		JsonArray knownNetworks = [];

		foreach (string cidr in request.KnownNetworks ?? Array.Empty<string>()) {
			knownNetworks.Add(cidr.Trim());
		}

		// Same shape as ASF-ui ipc-config.js / ASF wiki examples
		return new JsonObject {
			["Kestrel"] = new JsonObject {
				["Endpoints"] = new JsonObject {
					["HTTP"] = new JsonObject {
						["Url"] = url
					}
				},
				["PathBase"] = pathBase,
				["KnownNetworks"] = knownNetworks
			}
		};
	}

	private static bool TryParseCidr(string value, out (IPAddress Address, int Prefix) result) {
		result = default;

		string[] parts = value.Split('/', 2, StringSplitOptions.TrimEntries);

		if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out IPAddress? address) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)) {
			return false;
		}

		int maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;

		if (prefix is < 0 || prefix > maxPrefix) {
			return false;
		}

		result = (address, prefix);

		return true;
	}
}
