using Ktuvit.Plugin.Helpers;
using Ktuvit.Plugin.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ktuvit.Plugin
{
    public class KtuvitSubtitleProvider : ISubtitleProvider
    {
        private readonly ILogger _logger;
        private KtuvitExplorer _ktuvitExplorer;
        private ImdbExplorer _imdbExplorer;

        public KtuvitSubtitleProvider(
            ILogger logger )
        {
            _logger = logger;
        }

        public string Name => "Ktuvit";

        public IEnumerable<string> CurrentSupportedLanguages => new[] { "he", "heb" };
        public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Episode, VideoContentType.Movie };

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            // Initialize explorers            
            _ktuvitExplorer = KtuvitExplorer.Instance;
            _imdbExplorer = ImdbExplorer.Instance;

            if (string.IsNullOrEmpty(request.Language) || !string.Equals(request.Language, "he", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Ktuvit: Subtitle search was initiated for non-Hebrew language. Ktuvit exiting...");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }
            else
            {
                _logger.Info($"Ktuvit: Hebrew subtitle search was initiated.");
            }

            try
            {
                var searchResults = new List<RemoteSubtitleInfo>();

                if (request.ContentType == VideoContentType.Movie)
                {
                    _logger.Info($"Ktuvit: Subtitle request type was detected as Movie");
                    searchResults.AddRange(await SearchMovies(request, cancellationToken));
                }
                else if (request.ContentType == VideoContentType.Episode)
                {
                    _logger.Info($"Ktuvit: Subtitle request type was detected as Series");
                    searchResults.AddRange(await SearchEpisodes(request, cancellationToken));
                }

                return searchResults;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error searching for subtitles in Ktuvit. {ex}");
                return Enumerable.Empty<RemoteSubtitleInfo>();
            }
        }

        private async Task<IEnumerable<RemoteSubtitleInfo>> SearchMovies(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            // Structured request data
            var searchPhrase = RequestParser.GetMovieSearchPhrase(request);

            // Get Movie Ktuvit ID
            var ktuvitId = await _ktuvitExplorer.GetKtuvitId(searchPhrase.MovieName, searchPhrase.KtuvitSearchId, searchPhrase.ImdbId);

            // Search for subtitles
            _logger.Info($"Ktuvit: Searching subtitles for Movie: {searchPhrase.MovieName}, ImdbID: {searchPhrase.ImdbId}");
            return await _ktuvitExplorer.GetMovieRemoteSubtitles(ktuvitId);
        }

        private async Task<IEnumerable<RemoteSubtitleInfo>> SearchEpisodes(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {

            // Structured request data
            var searchPhrase = RequestParser.GetSeriesSearchPhrase(request);

            // Get Series Ktuvit ID
            var seriesImdbId = await _imdbExplorer.GetSeriesImdbId(searchPhrase.ImdbId);
            var ktuvitId = await _ktuvitExplorer.GetKtuvitId(searchPhrase.SeriesName, searchPhrase.KtuvitSearchId, seriesImdbId);

            // Search for subtitles
            _logger.Info($"Ktuvit: Searching subtitles for Series: {searchPhrase.SeriesName}, Season: {searchPhrase.SeasonIndex}, Episode: {searchPhrase.EpisodeIndex}, ImdbID: {searchPhrase.ImdbId}");
            return await _ktuvitExplorer.GetSeriesRemoteSubtitles(searchPhrase, ktuvitId);
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            _logger.Info($"Ktuvit: User Requested downloading subtitle with ID: {id}");

            string[] subtitleInfo = id.Split(':');
            string subtitleId = subtitleInfo[0];
            string filmId = subtitleInfo[1];

            return await _ktuvitExplorer.DownloadSubtitle(filmId, subtitleId);
        }
    }
}