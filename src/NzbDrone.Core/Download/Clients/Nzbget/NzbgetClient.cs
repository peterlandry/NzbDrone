﻿using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients.Nzbget
{
    public class NzbgetClient : IDownloadClient
    {
        private readonly IConfigService _configService;
        private readonly IHttpProvider _httpProvider;
        private readonly IParsingService _parsingService;
        private readonly Logger _logger;

        public NzbgetClient(IConfigService configService, IHttpProvider httpProvider, IParsingService parsingService, Logger logger)
        {
            _configService = configService;
            _httpProvider = httpProvider;
            _parsingService = parsingService;
            _logger = logger;
        }

        public string DownloadNzb(RemoteEpisode remoteEpisode)
        {
            var url = remoteEpisode.Release.DownloadUrl;
            var title = remoteEpisode.Release.Title + ".nzb";

            string cat = _configService.NzbgetTvCategory;
            int priority = remoteEpisode.IsRecentEpisode() ? (int)_configService.NzbgetRecentTvPriority : (int)_configService.NzbgetOlderTvPriority;

            var command = new JsonRequest
            {
                Method = "appendurl",
                Params = new object[] { title, cat, priority, false, url }
            };

            _logger.Info("Adding report [{0}] to the queue.", title);
            var response = PostCommand(command.ToJson());

            CheckForError(response);

            var success = Json.Deserialize<EnqueueResponse>(response).Result;
            _logger.Debug("Queue Response: [{0}]", success);

            return null;
        }

        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_configService.NzbgetHost) && _configService.NzbgetPort != 0;
            }
        }

        public virtual IEnumerable<QueueItem> GetQueue()
        {
            var command = new JsonRequest
                {
                    Method = "listgroups",
                    Params = null
                };

            var response = PostCommand(command.ToJson());

            CheckForError(response);

            var itmes = Json.Deserialize<NzbGetQueue>(response).QueueItems;

            foreach (var nzbGetQueueItem in itmes)
            {
                var queueItem = new QueueItem();
                queueItem.Id = nzbGetQueueItem.NzbId.ToString();
                queueItem.Title = nzbGetQueueItem.NzbName;
                queueItem.Size = nzbGetQueueItem.FileSizeMb;
                queueItem.Sizeleft = nzbGetQueueItem.RemainingSizeMb;

                var parsedEpisodeInfo = Parser.Parser.ParseTitle(queueItem.Title);
                if (parsedEpisodeInfo == null) continue;

                var remoteEpisode = _parsingService.Map(parsedEpisodeInfo, 0);
                if (remoteEpisode.Series == null) continue;

                queueItem.RemoteEpisode = remoteEpisode;

                yield return queueItem;
            }
        }

        public IEnumerable<HistoryItem> GetHistory(int start = 0, int limit = 0)
        {
            return new HistoryItem[0];
        }

        public void RemoveFromQueue(string id)
        {
            throw new NotImplementedException();
        }

        public void RemoveFromHistory(string id)
        {
            throw new NotImplementedException();
        }

        public virtual VersionModel GetVersion(string host = null, int port = 0, string username = null, string password = null)
        {
            //Get saved values if any of these are defaults
            if (host == null)
                host = _configService.NzbgetHost;

            if (port == 0)
                port = _configService.NzbgetPort;

            if (username == null)
                username = _configService.NzbgetUsername;

            if (password == null)
                password = _configService.NzbgetPassword;

            var command = new JsonRequest
            {
                Method = "version",
                Params = null
            };

            var address = String.Format(@"{0}:{1}", host, port);
            var response = _httpProvider.PostCommand(address, username, password, command.ToJson());

            CheckForError(response);

            return Json.Deserialize<VersionModel>(response);
        }

        public virtual string Test(string host, int port, string username, string password)
        {
            try
            {
                var version = GetVersion(host, port, username, password);
                return version.Result;
            }
            catch (Exception ex)
            {
                _logger.DebugException("Failed to Test Nzbget", ex);
            }

            return String.Empty;
        }

        private string PostCommand(string command)
        {
            var url = String.Format(@"{0}:{1}",
                                 _configService.NzbgetHost,
                                 _configService.NzbgetPort);

            return _httpProvider.PostCommand(url, _configService.NzbgetUsername, _configService.NzbgetPassword, command);
        }

        private void CheckForError(string response)
        {
            var result = Json.Deserialize<JsonError>(response);

            if (result.Error != null)
                throw new ApplicationException(result.Error.ToString());
        }
    }
}