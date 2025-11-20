using Ktuvit.Plugin.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace Ktuvit.Plugin.Helpers
{

    public class ImdbExplorer
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;

        public ImdbExplorer(
            IJsonSerializer jsonSerializer,
            ILogger logger,
            IHttpClient httpClient)
        {
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _httpClient = httpClient;
        }

        // Singleton implementation
        private static ImdbExplorer _instance;
        private static readonly object _lock = new();

        public static ImdbExplorer Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance;
                }
            }
        }

        public static void Initialize(IJsonSerializer jsonSerializer, ILogger logger, IHttpClient httpClient)
        {
            lock (_lock)
            {
                _instance ??= new ImdbExplorer(jsonSerializer, logger, httpClient);
            }
        }

        private const string ImdbUrl = "https://www.imdb.com";

        public async Task<string> GetSeriesImdbId(string episodeImdbId)
        {
            _logger.Info($"Ktuvit: Getting Series IMDB ID from episode IMDB ID: {episodeImdbId}");
            if (string.IsNullOrEmpty(episodeImdbId) || episodeImdbId.Length < 3)
            {
                return null;
            }
            var httpRequest = new HttpRequestOptions();
            httpRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0 Safari/537.36";
            httpRequest.Url = $"{ImdbUrl}/title/{episodeImdbId}";
            var response = await _httpClient.GetResponse(httpRequest);
            string htmlContent;
            using (var reader = new StreamReader(response.Content, Encoding.UTF8))
            {
                htmlContent = await reader.ReadToEndAsync();
            }
            var imdbIdMatch = Regex.Match(htmlContent, @"""series"":\{""id"":""(tt\d+)""");
            string imdbId = imdbIdMatch.Success ? imdbIdMatch.Groups[1].Value : null;
            _logger.Info($"Ktuvit: Series IMDb ID: {imdbId}");
            return imdbId;
        }

    }
}
