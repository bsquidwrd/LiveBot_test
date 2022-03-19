﻿using Discord;
using Discord.Interactions;
using Discord.Rest;
using LiveBot.Core.Repository.Interfaces;
using LiveBot.Core.Repository.Interfaces.Monitor;
using LiveBot.Discord.SlashCommands.Helpers;
using LiveBot.Discord.SlashCommands.Modules;
using LiveBot.Repository;
using LiveBot.Watcher.Twitch;
using System.Reflection;

namespace LiveBot.Discord.SlashCommands
{
    public static class LiveBotExtensions
    {
        /// <summary>
        /// Adds EnvironmentVariables to config, configures and adds
        /// <see cref="DiscordRestClient"/> and <see cref="InteractionService"/>
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static async Task<WebApplicationBuilder> SetupLiveBot(this WebApplicationBuilder builder)
        {
            builder.Configuration.AddEnvironmentVariables(prefix: "LiveBot_");

            builder.Services.AddScoped<LiveBotDBContext>(_ => new LiveBotDBContext(builder.Configuration.GetValue<string>("connectionstring")));
            builder.Services.AddSingleton<IUnitOfWorkFactory>(new UnitOfWorkFactory(builder.Configuration));

            var IsDebug = builder.Configuration.GetValue<bool>("IsDebug", false);
            var discordLogLevel = IsDebug ? LogSeverity.Verbose : LogSeverity.Info;
            var discord = new DiscordRestClient(new DiscordRestConfig()
            {
                LogLevel = discordLogLevel
            });
            var token = builder.Configuration.GetValue<string>("token");
            await discord.LoginAsync(TokenType.Bot, token);

            builder.Services.AddRouting();
            builder.Services.AddSingleton(discord);
            builder.Services.AddInteractionService(config =>
            {
                config.UseCompiledLambda = true;
                config.LogLevel = discordLogLevel;
            });

            // Setup MassTransit
            builder.Services.AddLiveBotQueueing();

            // Setup Monitors
            builder.Services.AddSingleton<ILiveBotMonitor, TwitchMonitor>();

            return builder;
        }

        /// <summary>
        /// Registers bot Commands, maps http path for receiving Slash Commands
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static async Task<WebApplication> RegisterLiveBot(this WebApplication app)
        {
            app.Services.GetRequiredService<IUnitOfWorkFactory>().Migrate();

            var commands = app.Services.GetRequiredService<InteractionService>();
            commands.AddTypeConverter<Uri>(new UriConverter());
            await commands.AddModulesAsync(Assembly.GetExecutingAssembly(), app.Services);

            var IsDebug = app.Configuration.GetValue<bool>("IsDebug", false);
            var adminGuildId = app.Configuration.GetValue<ulong>("adminguild");

            if (IsDebug)
            {
                app.Logger.LogInformation("Starting bot in debug mode...");
                var testGuildId = app.Configuration.GetValue<ulong>("testguild");
                adminGuildId = testGuildId;
                await commands.RegisterCommandsToGuildAsync(guildId: testGuildId, deleteMissing: true);
            }
            else
            {
                await commands.RegisterCommandsGloballyAsync(deleteMissing: true);
            }

            var adminGuild = await commands.RestClient.GetGuildAsync(adminGuildId);
            await commands.AddModulesToGuildAsync(adminGuild, modules: commands.GetModuleInfo<AdminModule>());

            app.MapInteractionService("/interactions", app.Configuration.GetValue<string>("publickey"));

            foreach (var monitor in app.Services.GetServices<ILiveBotMonitor>())
            {
                await monitor.StartAsync(IsWatcher: false);
                app.Logger.LogInformation("Started {monitor} monitor", monitor.ServiceType);
            }

            return app;
        }
    }
}