﻿using System.Threading.Tasks;

namespace LiveBot.Core.Repository.Interfaces.Stream
{
    /// <summary>
    /// Represents a Monitoring Service that the bot can notify about
    /// </summary>
    public interface ILiveBotMonitor : ILiveBotBase
    {
        public string URLPattern { get; set; }

        /// <summary>
        /// Represents the Starting class for a Monitoring Service to function
        /// </summary>
        /// <returns>The class used to start a Monitoring Service</returns>
        public ILiveBotMonitorStart GetStartClass();

        /// <summary>
        /// Returns a <c>ILiveBotUser</c> object based on the given <paramref name="userId"/> or <paramref name="username"/>
        /// </summary>
        /// <param name="username"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public Task<ILiveBotUser> GetUser(string username = null, string userId = null);

        /// <summary>
        /// Returns a <c>ILiveBotStream</c> based on the given <paramref name="user"/>
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public Task<ILiveBotStream> GetStream(ILiveBotUser user);

        /// <summary>
        /// Returns a <c>ILiveBotGame</c> based on the given <paramref name="gameId"/>
        /// </summary>
        /// <param name="gameId"></param>
        /// <returns></returns>
        public Task<ILiveBotGame> GetGame(string gameId);

        /// <summary>
        /// Checks that a given URL is valid based on URLPattern
        /// </summary>
        /// <see cref="URLPattern"/>
        /// <param name="streamURL"></param>
        /// <returns></returns>
        public bool IsValid(string streamURL);
    }
}