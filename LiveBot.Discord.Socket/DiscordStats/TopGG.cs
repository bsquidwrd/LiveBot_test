﻿using Discord;
using Discord.WebSocket;
using LiveBot.Core.Interfaces.Discord;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveBot.Discord.Socket.DiscordStats
{
    internal class TopGGPayload
    {
        [JsonPropertyName("server_count")]
        public int guildCount { get; set; }

        public TopGGPayload(int count)
        {
            guildCount = count;
        }
    }

    public class TopGG : IDiscordStats
    {
        private readonly ILogger<TopGG> _logger;
        private readonly IConfiguration _configuration;
        private readonly DiscordShardedClient _discordClient;
        private System.Timers.Timer? _timer = null;
        private readonly bool IsDebug = false;

        private readonly string SiteName = "TopGG";
        private readonly string ApiConfigName = "TopGG_API";
        private readonly string UpdateUrl = "https://top.gg/api/bots/{BotId}/stats";

        public TopGG(ILogger<TopGG> logger, IConfiguration configuration, DiscordShardedClient discordClient)
        {
            _logger = logger;
            _configuration = configuration;
            _discordClient = discordClient;

            IsDebug = _configuration.GetValue<bool>("IsDebug", false);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!IsDebug)
            {
                TimeSpan timeSpan = TimeSpan.FromMinutes(5);
                var timer = new System.Timers.Timer(timeSpan.TotalMilliseconds)
                {
                    AutoReset = true
                };
                timer.Elapsed += async (sender, e) => await UpdateStats();
                _timer = timer;
                _timer.Start();
            }
            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Stop();
            _timer = null;
            return Task.CompletedTask;
        }

        public async Task UpdateStats()
        {
            if (IsDebug || _discordClient.LoginState != LoginState.LoggedIn || _discordClient.CurrentUser?.Id == null)
                return;

            var guilds = _discordClient.Guilds;
            var payload = new TopGGPayload(guilds.Count);
            var apiKey = _configuration.GetValue<string>(ApiConfigName);
            if (apiKey == null)
                return;

            HttpClient httpClient = new();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", apiKey);
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var endpoint = UpdateUrl.Replace("{BotId}", _discordClient.CurrentUser.Id.ToString());
                var response = await httpClient.PostAsync(requestUri: endpoint, content: content);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation(message: "Updated Guild Count for {StatsSiteName}: {GuildCount}", SiteName, guilds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(exception: ex, message: "Unable to update stats for {StatsSiteName}", SiteName);
            }
            finally
            {
                httpClient.Dispose();
            }
        }
    }
}