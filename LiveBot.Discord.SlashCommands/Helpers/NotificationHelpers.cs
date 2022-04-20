﻿using Discord;
using LiveBot.Core.Repository.Interfaces.Monitor;
using LiveBot.Core.Repository.Models.Streams;
using LiveBot.Core.Repository.Static;
using System.Globalization;

namespace LiveBot.Discord.SlashCommands.Helpers
{
    public static class NotificationHelpers
    {
        public static string EscapeSpecialDiscordCharacters(string input)
        {
            return Format.Sanitize(input);
        }

        /// <summary>
        /// Formats a notification string with the necessary parameters
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public static string GetNotificationMessage(ILiveBotStream stream, StreamSubscription subscription, ILiveBotUser? user = null, ILiveBotGame? game = null)
        {
            string RoleMentions = "";
            if (subscription.RolesToMention.Any())
                RoleMentions = String.Join(" ", subscription.RolesToMention.OrderBy(i => i.DiscordRoleId).Select(i => i.DiscordRoleId).Distinct().Select(i => MentionUtils.MentionRole(i)));

            var tempUser = user ?? stream.User;
            var tempGame = game ?? stream.Game;

            return subscription.Message
                .Replace("{Name}", EscapeSpecialDiscordCharacters(tempUser.DisplayName), ignoreCase: true, culture: CultureInfo.CurrentCulture)
                .Replace("{Username}", EscapeSpecialDiscordCharacters(tempUser.DisplayName), ignoreCase: true, culture: CultureInfo.CurrentCulture)
                .Replace("{Game}", EscapeSpecialDiscordCharacters(tempGame.Name), ignoreCase: true, culture: CultureInfo.CurrentCulture)
                .Replace("{Title}", EscapeSpecialDiscordCharacters(stream.Title), ignoreCase: true, culture: CultureInfo.CurrentCulture)
                .Replace("{URL}", Format.EscapeUrl(stream.StreamURL) ?? "", ignoreCase: true, culture: CultureInfo.CurrentCulture)
                .Replace("{Role}", RoleMentions, ignoreCase: true, culture: CultureInfo.CurrentCulture)
                .Trim();
        }

        /// <summary>
        /// Generates a Discord Embed for the given <paramref name="stream"/>
        /// </summary>
        /// <param name="stream"></param>
        /// <returns>Discord Embed with Stream Information</returns>
        public static Embed GetStreamEmbed(ILiveBotStream stream, ILiveBotUser user, ILiveBotGame game)
        {
            // Build the Author of the Embed
            var authorBuilder = new EmbedAuthorBuilder()
                .WithName(user.DisplayName)
                .WithIconUrl(user.AvatarURL)
                .WithUrl(user.ProfileURL);

            // Build the Footer of the Embed
            var footerBuilder = new EmbedFooterBuilder()
                .WithText("Stream start time");

            // Add Basic information to EmbedBuilder
            var builder = new EmbedBuilder()
                .WithColor(stream.ServiceType.GetAlertColor())
                .WithAuthor(authorBuilder)
                .WithFooter(footerBuilder)
                .WithTimestamp(stream.StartTime)
                .WithDescription(EscapeSpecialDiscordCharacters(stream.Title))
                .WithUrl(stream.StreamURL)
                .WithThumbnailUrl(user.AvatarURL);

            // Add Game field
            builder.AddField(name: "Game", value: game.Name, inline: true);

            // Add Stream URL field
            builder.AddField(name: "Stream", value: stream.StreamURL, inline: true);

            // Add Status Field
            //builder.AddField(name: "Status", value: "", inline: false);

            return builder.Build();
        }
    }
}