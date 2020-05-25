﻿namespace LiveBot.Core.Repository.Interfaces.Stream
{
    /// <summary>
    /// Represents a generic User for use within the bot, usually returned by a Monitoring Service
    /// </summary>
    public interface ILiveBotUser : ILiveBotBase
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string BroadcasterType { get; set; }
        public string AvatarURL { get; set; }

        /// <summary>
        /// Gets a URL to the Users profile
        /// </summary>
        /// <returns></returns>
        public string GetProfileURL();
    }
}