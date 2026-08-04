using System;
using System.Composition;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace IpcConfig;

/// <summary>
/// Exposes authenticated IPC endpoints to read/write <c>config/IPC.config</c>.
/// Implements GitHub updates so ASF can refresh the plugin after first install.
/// </summary>
[Export(typeof(IPlugin))]
internal sealed class IpcConfigPlugin : IGitHubPluginUpdates, IWebServiceProvider {
	[JsonInclude]
	[JsonRequired]
	public string Name => nameof(IpcConfig);

	[JsonInclude]
	[JsonRequired]
	public Version Version => typeof(IpcConfigPlugin).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

	/// <summary>GitHub repo that publishes IpcConfig.zip releases.</summary>
	[JsonInclude]
	[JsonRequired]
	public string RepositoryName => "Jeremias0618/ASF-Plugin";

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} loaded. Endpoints: GET|PUT|DELETE /Api/IpcConfig");

		return Task.CompletedTask;
	}

	public void OnConfiguringServices(IServiceCollection services) {
		ArgumentNullException.ThrowIfNull(services);

		services.AddSingleton<IpcConfigFileService>();
	}

	public void OnConfiguringEndpoints(IApplicationBuilder app) => ArgumentNullException.ThrowIfNull(app);
}
