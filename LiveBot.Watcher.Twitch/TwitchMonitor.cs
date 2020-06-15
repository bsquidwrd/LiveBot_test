﻿using LiveBot.Core.Repository.Base.Monitor;
using LiveBot.Core.Repository.Interfaces.Monitor;
using LiveBot.Core.Repository.Models.Streams;
using LiveBot.Core.Repository.Static;
using LiveBot.Watcher.Twitch.Contracts;
using LiveBot.Watcher.Twitch.Models;
using MassTransit;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using TwitchLib.Api;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Core.RateLimiter;
using TwitchLib.Api.Helix.Models.Games;
using TwitchLib.Api.Helix.Models.Streams;
using TwitchLib.Api.Helix.Models.Users;
using TwitchLib.Api.Services;
using TwitchLib.Api.Services.Events;
using TwitchLib.Api.Services.Events.LiveStreamMonitor;
using TwitchLib.Api.V5.Models.Auth;

namespace LiveBot.Watcher.Twitch
{
    public class TwitchMonitor : BaseLiveBotMonitor
    {
        public LiveStreamMonitorService Monitor;
        public TwitchAPI API;
        public IServiceProvider services;
        public IBusControl _bus;
        private readonly int RetryDelay = 1000 * 30; // 30 seconds
        private readonly int ApiRetryCount = 5; // How many times to retry API requests

        public string ClientId
        {
            get => API.Settings.ClientId;
            set
            {
                API.Settings.ClientId = value;
            }
        }

        public string ClientSecret
        {
            get => API.Settings.Secret;
            set
            {
                API.Settings.Secret = value;
            }
        }

        public string AccessToken
        {
            get => API.Settings.AccessToken;
            set
            {
                API.Settings.AccessToken = value;
            }
        }

        public Timer RefreshAuthTimer;

        // My caches
        //private List<ILiveBotGame> _gameCache = new List<ILiveBotGame>();
        //private List<ILiveBotUser> _userCache = new List<ILiveBotUser>();
        private ConcurrentBag<ILiveBotGame> _gameCache = new ConcurrentBag<ILiveBotGame>();
        private ConcurrentBag<ILiveBotUser> _userCache = new ConcurrentBag<ILiveBotUser>();

        /// <summary>
        /// Represents the whole Service for Twitch Monitoring
        /// </summary>
        public TwitchMonitor()
        {
            StartTime = DateTime.UtcNow;
            BaseURL = "https://twitch.tv";
            ServiceType = ServiceEnum.TWITCH;
            URLPattern = "^((http|https):\\/\\/|)([\\w\\d]+\\.)?twitch\\.tv/(?<username>[a-zA-Z0-9_]{1,})";

            var rateLimiter = TimeLimiter.GetFromMaxCountByInterval(5000, TimeSpan.FromMinutes(1));
            API = new TwitchAPI(rateLimiter: rateLimiter);
            Monitor = new LiveStreamMonitorService(api: API, checkIntervalInSeconds: 60, maxStreamRequestCountPerRequest: 100);

            Monitor.OnServiceStarted += Monitor_OnServiceStarted;
            Monitor.OnStreamOnline += Monitor_OnStreamOnline;
            Monitor.OnStreamOffline += Monitor_OnStreamOffline;
            Monitor.OnStreamUpdate += Monitor_OnStreamUpdate;
        }

        #region Events

        public void Monitor_OnServiceStarted(object sender, OnServiceStartedArgs e)
        {
            Log.Debug("Monitor service successfully connected to Twitch!");
            //await _PublishTwitchUpdateUsers();
        }

        public async void Monitor_OnStreamOnline(object sender, OnStreamOnlineArgs e)
        {
            ILiveBotStream stream = await GetStream(e.Stream);
            //await _PublishUpdateUser(stream.User);
            await _PublishStreamOnline(stream);
        }

        public async void Monitor_OnStreamUpdate(object sender, OnStreamUpdateArgs e)
        {
            ILiveBotStream stream = await GetStream(e.Stream);
            await _PublishStreamOffline(stream);
        }

        public async void Monitor_OnStreamOffline(object sender, OnStreamOfflineArgs e)
        {
            ILiveBotStream stream = await GetStream(e.Stream);
            await _PublishStreamOffline(stream);
        }

        #endregion Events

        #region API Calls

        private async Task<Game> API_GetGame(string gameId, int retryCount = 0)
        {
            try
            {
                List<string> gameIDs = new List<string> { gameId };
                GetGamesResponse games = await API.Helix.Games.GetGamesAsync(gameIds: gameIDs).ConfigureAwait(false);
                return games.Games.FirstOrDefault(i => i.Id == gameId);
            }
            catch (Exception e) when (e is BadGatewayException || e is InternalServerErrorException)
            {
                Log.Error($"{e}");
                if (retryCount <= ApiRetryCount)
                {
                    await Task.Delay(RetryDelay);
                    return await API_GetGame(gameId, retryCount + 1);
                }
                return null;
            }
            catch (Exception e) when (e is InvalidCredentialException || e is BadScopeException)
            {
                Log.Error($"{e}");
                await UpdateAuth();
                await Task.Delay(RetryDelay);
                return await API_GetGame(gameId);
            }
        }

        private async Task<User> API_GetUserByLogin(string username, int retryCount = 0)
        {
            try
            {
                List<string> usernameList = new List<string> { username };
                GetUsersResponse apiUser = await API.Helix.Users.GetUsersAsync(logins: usernameList).ConfigureAwait(false);
                return apiUser.Users.FirstOrDefault(i => i.Login == username);
            }
            catch (Exception e) when (e is BadGatewayException || e is InternalServerErrorException)
            {
                Log.Error($"{e}");
                if (retryCount <= ApiRetryCount)
                {
                    await Task.Delay(RetryDelay);
                    return await API_GetUserByLogin(username, retryCount + 1);
                }
                return null;
            }
            catch (Exception e) when (e is InvalidCredentialException || e is BadScopeException)
            {
                Log.Error($"{e}");
                await UpdateAuth();
                await Task.Delay(RetryDelay);
                return await API_GetUserByLogin(username);
            }
        }

        private async Task<User> API_GetUserById(string userId, int retryCount = 0)
        {
            try
            {
                List<string> userIdList = new List<string> { userId };
                GetUsersResponse apiUser = await API.Helix.Users.GetUsersAsync(ids: userIdList).ConfigureAwait(false);
                return apiUser.Users.FirstOrDefault(i => i.Id == userId);
            }
            catch (Exception e) when (e is BadGatewayException || e is InternalServerErrorException)
            {
                Log.Error($"{e}");
                if (retryCount <= ApiRetryCount)
                {
                    await Task.Delay(RetryDelay);
                    return await API_GetUserById(userId, retryCount + 1);
                }
                return null;
            }
            catch (Exception e) when (e is InvalidCredentialException || e is BadScopeException)
            {
                Log.Error($"{e}");
                await UpdateAuth();
                await Task.Delay(RetryDelay);
                return await API_GetUserById(userId);
            }
        }

        private async Task<GetUsersResponse> API_GetUsersById(List<string> userIdList, int retryCount = 0)
        {
            try
            {
                GetUsersResponse apiUsers = await API.Helix.Users.GetUsersAsync(ids: userIdList).ConfigureAwait(false);
                return apiUsers;
            }
            catch (Exception e) when (e is BadGatewayException || e is InternalServerErrorException)
            {
                Log.Error($"{e}");
                if (retryCount <= ApiRetryCount)
                {
                    await Task.Delay(RetryDelay);
                    return await API_GetUsersById(userIdList, retryCount + 1);
                }
                return null;
            }
            catch (Exception e) when (e is InvalidCredentialException || e is BadScopeException)
            {
                Log.Error($"{e}");
                await UpdateAuth();
                await Task.Delay(RetryDelay);
                return await API_GetUsersById(userIdList);
            }
        }

        private async Task<User> API_GetUserByURL(string url, int retryCount = 0)
        {
            try
            {
                string username = GetURLRegex(URLPattern).Match(url).Groups["username"].ToString();
                return await API_GetUserByLogin(username: username);
            }
            catch (Exception e) when (e is BadGatewayException || e is InternalServerErrorException)
            {
                Log.Error($"{e}");
                if (retryCount <= ApiRetryCount)
                {
                    await Task.Delay(RetryDelay);
                    return await API_GetUserByURL(url, retryCount + 1);
                }
                return null;
            }
            catch (Exception e) when (e is InvalidCredentialException || e is BadScopeException)
            {
                Log.Error($"{e}");
                await UpdateAuth();
                await Task.Delay(RetryDelay);
                return await API_GetUserByURL(url);
            }
        }

        private async Task<Stream> API_GetStream(string userId, int retryCount = 0)
        {
            try
            {
                List<string> userIdList = new List<string> { userId };
                GetStreamsResponse streams = await API.Helix.Streams.GetStreamsAsync(userIds: userIdList);
                return streams.Streams.FirstOrDefault(i => i.UserId == userId);
            }
            catch (Exception e) when (e is BadGatewayException || e is InternalServerErrorException)
            {
                Log.Error($"{e}");
                if (retryCount <= ApiRetryCount)
                {
                    await Task.Delay(RetryDelay);
                    return await API_GetStream(userId, retryCount + 1);
                }
                return null;
            }
            catch (Exception e) when (e is InvalidCredentialException || e is BadScopeException)
            {
                Log.Error($"{e}");
                await UpdateAuth();
                await Task.Delay(RetryDelay);
                return await API_GetStream(userId);
            }
        }

        #endregion API Calls

        #region Misc Functions

        public async Task<ILiveBotStream> GetStream(Stream stream)
        {
            ILiveBotUser liveBotUser = await GetUser(userId: stream.UserId);
            ILiveBotGame liveBotGame = await GetGame(gameId: stream.GameId);
            return new TwitchStream(BaseURL, ServiceType, stream, liveBotUser, liveBotGame);
        }

        private static IEnumerable<List<T>> SplitList<T>(List<T> locations, int nSize = 30)
        {
            for (int i = 0; i < locations.Count; i += nSize)
            {
                yield return locations.GetRange(i, Math.Min(nSize, locations.Count - i));
            }
        }

        public async Task UpdateAuth()
        {
            Log.Debug($"Refreshing Auth for {ServiceType}");
            var oldAuth = await _work.AuthRepository.SingleOrDefaultAsync(i => i.ServiceType == ServiceType && i.ClientId == ClientId && i.Expired == false);
            RefreshResponse refreshResponse = await API.V5.Auth.RefreshAuthTokenAsync(refreshToken: oldAuth.RefreshToken, clientSecret: ClientSecret, clientId: ClientId);

            var newAuth = new TwitchAuth(ServiceType, ClientId, refreshResponse);
            await _work.AuthRepository.AddOrUpdateAsync(newAuth, i => i.ServiceType == ServiceType && i.ClientId == ClientId && i.AccessToken == newAuth.AccessToken);

            oldAuth.Expired = true;
            await _work.AuthRepository.AddOrUpdateAsync(oldAuth, i => i.ServiceType == ServiceType && i.ClientId == ClientId && i.AccessToken == oldAuth.AccessToken);
            AccessToken = newAuth.AccessToken;

            TimeSpan refreshAuthTimeSpan = TimeSpan.FromSeconds(refreshResponse.ExpiresIn);
            // Trigger it 5 minutes before expiration time to be safe
            SetupAuthTimer(refreshAuthTimeSpan.Subtract(TimeSpan.FromMinutes(5)));
        }

        private void SetupAuthTimer(TimeSpan timeSpan)
        {
            if (RefreshAuthTimer != null)
                RefreshAuthTimer.Stop();

            RefreshAuthTimer = new Timer(timeSpan.TotalMilliseconds)
            {
                AutoReset = false
            };
            RefreshAuthTimer.Elapsed += async (sender, e) => await UpdateAuth();
            RefreshAuthTimer.Start();
        }

        public async Task UpdateUsers()
        {
            try
            {
                foreach (List<string> userIds in SplitList(Monitor.ChannelsToMonitor))
                {
                    GetUsersResponse users = await API_GetUsersById(userIds);
                    foreach (User user in users.Users)
                    {
                        TwitchUser twitchUser = new TwitchUser(BaseURL, ServiceType, user);
                        StreamUser streamUser = await _work.UserRepository.SingleOrDefaultAsync(i => i.ServiceType == ServiceType && i.SourceID == user.Id);
                        streamUser.Username = twitchUser.Username;
                        streamUser.DisplayName = twitchUser.DisplayName;
                        streamUser.AvatarURL = twitchUser.AvatarURL;
                        streamUser.ProfileURL = twitchUser.ProfileURL;

                        await _work.UserRepository.AddOrUpdateAsync(streamUser, (i => i.ServiceType == streamUser.ServiceType && i.SourceID == streamUser.SourceID));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error($"{e}");
            }
        }

        #endregion Misc Functions

        #region Messaging Implementation

        public async Task _PublishStreamOnline(ILiveBotStream stream)
        {
            try
            {
                await _bus.Publish(new TwitchStreamOnline { Stream = stream });
            }
            catch (Exception e)
            {
                Log.Error($"Error trying to publish StreamOnline:\n{e}");
            }
        }

        public async Task _PublishStreamUpdate(ILiveBotStream stream)
        {
            try
            {
                await _bus.Publish(new TwitchStreamUpdate { Stream = stream });
            }
            catch (Exception e)
            {
                Log.Error($"Error trying to publish StreamUpdate:\n{e}");
            }
        }

        public async Task _PublishStreamOffline(ILiveBotStream stream)
        {
            try
            {
                await _bus.Publish(new TwitchStreamOffline { Stream = stream });
            }
            catch (Exception e)
            {
                Log.Error($"Error trying to publish StreamOffline:\n{e}");
            }
        }

        public async Task _PublishTwitchUpdateUsers()
        {
            try
            {
                await _bus.Publish(new TwitchUpdateUsers { ServiceType = ServiceType });
            }
            catch (Exception e)
            {
                Log.Error($"Error trying to publish TwitchUpdateUsers:\n{e}");
                await Task.Delay(RetryDelay);
                await _PublishTwitchUpdateUsers();
            }
        }

        #endregion Messaging Implementation

        #region Interface Requirements

        /// <inheritdoc/>
        public override ILiveBotMonitorStart GetStartClass()
        {
            return new TwitchStart();
        }

        /// <inheritdoc/>
        public override async Task<ILiveBotGame> GetGame(string gameId)
        {
            //var cachedGame = _gameCache.Where(i => i.Id == gameId).FirstOrDefault();
            var cachedGame = _gameCache.ToList().Where(i => i.Id == gameId).FirstOrDefault();
            if (cachedGame != null)
            {
                return cachedGame;
            }
            Game game = await API_GetGame(gameId);
            var twitchGame = new TwitchGame(BaseURL, ServiceType, game);
            _gameCache.Add(twitchGame);
            return twitchGame;
        }

        /// <inheritdoc/>
        public override async Task<ILiveBotStream> GetStream(ILiveBotUser user)
        {
            Stream stream = await API_GetStream(user.Id);
            if (stream == null)
                return null;
            ILiveBotGame game = await GetGame(stream.GameId);
            return new TwitchStream(BaseURL, ServiceType, stream, user, game);
        }

        /// <inheritdoc/>
        public override async Task<ILiveBotUser> GetUserById(string userId)
        {
            //var cachedUser = _userCache.Where(i => i.Id == userId).FirstOrDefault();
            var cachedUser = _userCache.ToList().Where(i => i.Id == userId).FirstOrDefault();
            if (cachedUser != null)
            {
                return cachedUser;
            }
            User apiUser = await API_GetUserById(userId);
            var twitchUser = new TwitchUser(BaseURL, ServiceType, apiUser);
            _userCache.Add(twitchUser);
            return twitchUser;
        }

        /// <inheritdoc/>
        public override async Task<ILiveBotUser> GetUser(string username = null, string userId = null, string profileURL = null)
        {
            User apiUser;
            //var cachedUser = _userCache.Where(i => i.Id == userId || i.Username == username || i.ProfileURL == profileURL).FirstOrDefault();
            var cachedUser = _userCache.ToList().Where(i => i.Id == userId || i.Username == username || i.ProfileURL == profileURL).FirstOrDefault();
            if (cachedUser != null)
                return cachedUser;

            if (!string.IsNullOrEmpty(username))
            {
                apiUser = await API_GetUserByLogin(username: username);
            }
            else if (!string.IsNullOrEmpty(userId))
            {
                apiUser = await API_GetUserById(userId: userId);
            }
            else if (!string.IsNullOrEmpty(profileURL))
            {
                apiUser = await API_GetUserByURL(url: profileURL);
            }
            else
            {
                return null;
            }

            TwitchUser twitchUser = new TwitchUser(BaseURL, ServiceType, apiUser);
            _userCache.Add(twitchUser);
            return twitchUser;
        }

        /// <inheritdoc/>
        public override bool AddChannel(ILiveBotUser user)
        {
            var channels = Monitor.ChannelsToMonitor;
            if (!channels.Contains(user.Id))
            {
                channels.Add(user.Id);
                Monitor.SetChannelsById(channels);
            }

            if (Monitor.ChannelsToMonitor.Contains(user.Id))
                return true;
            return false;
        }

        /// <inheritdoc/>
        public override bool RemoveChannel(ILiveBotUser user)
        {
            var channels = Monitor.ChannelsToMonitor;
            if (channels.Contains(user.Id))
            {
                channels.Remove(user.Id);
                Monitor.SetChannelsById(channels);
            }

            if (!Monitor.ChannelsToMonitor.Contains(user.Id))
                return true;
            return false;
        }

        #endregion Interface Requirements
    }
}