using System;
using System.Collections.Generic;
using System.Composition;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ASFBotSocial.Services;
using SteamKit2;

namespace ASFBotSocial;

[Export(typeof(IPlugin))]
internal sealed class ASFBotSocialPlugin : IBotSteamClient {
	[JsonInclude]
	public string Name => nameof(ASFBotSocial);

	[JsonInclude]
	public Version Version => typeof(ASFBotSocialPlugin).Assembly.GetName().Version ?? new Version(1, 0, 0);

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} loaded. IPC prefix: /Api/BotSocial/{{botNames}}/");

		return Task.CompletedTask;
	}

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) => Task.CompletedTask;

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
		AchievementHandler handler = new();
		AchievementHandler.Register(bot, handler);

		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([handler]);
	}
}
