using Ktuvit.Plugin.Configuration;
using Ktuvit.Plugin.Model;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
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

    public class KtuvitExplorer
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;

        public KtuvitExplorer(
            IJsonSerializer jsonSerializer,
            ILogger logger,
            IHttpClient httpClient)
        {
            _jsonSerializer = jsonSerializer;
            _logger = logger;
            _httpClient = httpClient;
        }

        // Singleton implementation
        private static KtuvitExplorer _instance;
        private static readonly object _lock = new();

        public static KtuvitExplorer Instance
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
                _instance ??= new KtuvitExplorer(jsonSerializer, logger, httpClient);
            }
        }

        private const string ApiBaseUrl = "https://www.ktuvit.me";
        private const string LoginUrl = $"{ApiBaseUrl}/Services/MembershipService.svc/Login";
        private const string SearchUrl = $"{ApiBaseUrl}/Services/ContentProvider.svc/SearchPage_search";
        private const string SeriesUrl = $"{ApiBaseUrl}/Services/GetModuleAjax.ashx?moduleName=SubtitlesList";
        private const string RequestDownloadUrl = $"{ApiBaseUrl}/Services/ContentProvider.svc/RequestSubtitleDownload";
        private const string DownloadUrl = $"{ApiBaseUrl}/Services/DownloadFile.ashx";
        private const string MovieUrl = $"{ApiBaseUrl}/MovieInfo.aspx";

        public async Task<string> GetKtuvitId(string filmName, int searchType, string imdbId)
        {
            // searchType: 0 for movies, 1 for TV shows
            var requestBody = new
            {
                request = new
                {
                    FilmName = filmName,
                    Actors = new string[] { },
                    Studios = (string[])null,
                    Directors = new string[] { },
                    Genres = new string[] { },
                    Countries = new string[] { },
                    Languages = new string[] { },
                    Year = "",
                    Rating = new string[] { },
                    Page = 1,
                    SearchType = searchType,
                    WithSubsOnly = false
                }
            };
            var json = _jsonSerializer.SerializeToString(requestBody);

            _logger.Info($"Ktuvit: Searching for Ktuvit ID");

            var httpRequest = new HttpRequestOptions();
            httpRequest.Url = SearchUrl;
            httpRequest.RequestContentType = "application/json";
            httpRequest.RequestContent = json.ToCharArray().AsMemory();
            var response = await _httpClient.Post(httpRequest);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                // First deserialization to get the wrapper response
                var initialResponse = _jsonSerializer.DeserializeFromStream<KtuvitSearchResponse>(response.Content);
                if (initialResponse?.d != null)
                {
                    // Second deserialization to parse the nested JSON string
                    var searchResults = _jsonSerializer.DeserializeFromString<KtuvitSearchResult>(initialResponse.d);
                    if (searchResults?.Films != null)
                    {
                        _logger.Info($"Ktuvit: Found {searchResults.Films.Count} films in search results for '{filmName}'");
                        foreach (var film in searchResults.Films)
                        {
                            var extractedImdbId = film.IMDB_Link?.Split('/')[^2];
                            if (film.ImdbID == imdbId || extractedImdbId == imdbId)
                            {
                                _logger.Info($"Ktuvit: Match found for film '{filmName}' with Ktuvit ID: {film.ID}");
                                return film.ID;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        _logger.Info($"Ktuvit: No match found for film '{filmName}' with IMDb ID '{imdbId}'");
                    }
                }
            }
            return null;
        }
        public async Task<List<RemoteSubtitleInfo>> GetMovieRemoteSubtitles(string ktuvitId)
        {
            var results = new List<RemoteSubtitleInfo>();

            // Login is required for movie subtitles
            _logger.Info("Ktuvit: Authentication is required to search movie subtitles. Authenticating...");
            if (string.IsNullOrEmpty(Plugin.Instance.Options.Username) || string.IsNullOrEmpty(Plugin.Instance.Options.Password))
            {
                _logger.Info("Ktuvit: Username or password not provided in plugin configuration. Cannot search movie subtitles without authentication.");
                return results;
            }
            var loginResult = KtuvitAuthentication(Plugin.Instance.Options.Username, Plugin.Instance.Options.Password);
            if (!loginResult)
            {
                _logger.Error("Ktuvit: Cannot search movie subtitles without valid authentication.");
                return results;
            }
            _logger.Info("Ktuvit: Authentication successful. Proceeding to search movie subtitles.");
            string searchUrl = $"{MovieUrl}?ID={ktuvitId}";
            var httpRequest = new HttpRequestOptions();
            httpRequest.Url = searchUrl;
            var response = await _httpClient.GetResponse(httpRequest);
            string htmlContent;
            using (var reader = new StreamReader(response.Content, Encoding.UTF8))
            {
                htmlContent = await reader.ReadToEndAsync();
            }
            var subtitlesDetails = ExtractSubtitleDetails(htmlContent);
            foreach (var subtitle in subtitlesDetails)
            {
                results.Add(new RemoteSubtitleInfo
                {
                    Author = "Ktuvit.me",
                    Name = subtitle.Title,
                    ProviderName = Plugin.PluginName,
                    Id = $"{subtitle.SubtitleID}:{ktuvitId}",
                    Language = "he",
                    IsForced = false,
                    Format = "srt"
                });
            }
            return results;
        }
        public async Task<List<RemoteSubtitleInfo>>  GetSeriesRemoteSubtitles(KtuvitSeriesSearchRequest ktuvitSeriesSearchRequest, string ktuvitId)
        {
            var results = new List<RemoteSubtitleInfo>();
            string searchUrl = $"{SeriesUrl}&SeriesID={ktuvitId}&Season={ktuvitSeriesSearchRequest.SeasonIndex}&Episode={ktuvitSeriesSearchRequest.EpisodeIndex}";
            var httpRequest = new HttpRequestOptions();
            httpRequest.Url = searchUrl;
            var response = await _httpClient.GetResponse(httpRequest);
            string htmlContent;
            using (var reader = new StreamReader(response.Content, Encoding.UTF8))
            {
                htmlContent = await reader.ReadToEndAsync();
            }
            var subtitlesDetails = ExtractSubtitleDetails(htmlContent);
            foreach (var subtitle in subtitlesDetails)
            {
                results.Add(new RemoteSubtitleInfo
                {
                    Author = "Ktuvit.me",
                    Name = subtitle.Title,
                    ProviderName = Plugin.PluginName,
                    Id = $"{subtitle.SubtitleID}:{ktuvitId}",
                    Language = "he",
                    IsForced = false,
                    Format = "srt"
                });
            }
            return results;
        }
        private List<KtuvitSubtitleDetails> ExtractSubtitleDetails(string htmlContent)
        {
            List<KtuvitSubtitleDetails> subtitleDetails = new List<KtuvitSubtitleDetails>();
            var subtitles = new List<(string Name, string Id)>();
            var seenIds = new HashSet<string>();
            // Regex for subtitle name (first line in div)
            var nameRegex = new Regex(@"<div style="".+[0-9]{1,3}%;""\s*([^<\r\n]+)", RegexOptions.IgnoreCase);
            // Regex for download id (32 hex chars)
            var idRegex = new Regex(@"data-subtitle-id=""([A-Fa-f0-9]{32})""", RegexOptions.IgnoreCase);

            int pos = 0;
            while ((pos = htmlContent.IndexOf("<tr", pos, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                int endTr = htmlContent.IndexOf("</tr>", pos, StringComparison.OrdinalIgnoreCase);
                if (endTr == -1) break;
                string trBlock = htmlContent.Substring(pos, endTr - pos);

                // Find the <div style="float: right; width: 95%;
                string divTag = "<div style=\"float: right; width: 95%;\">";
                int divStart = trBlock.IndexOf(divTag, StringComparison.OrdinalIgnoreCase);
                if (divStart != -1)
                {
                    int nameStart = divStart + divTag.Length;
                    int brEnd = trBlock.IndexOf("<br", nameStart, StringComparison.OrdinalIgnoreCase);
                    if (brEnd != -1)
                    {
                        string name = trBlock.Substring(nameStart, brEnd - nameStart).Trim();
                        // Extract first download id (avoid duplicates)
                        var idMatch = idRegex.Match(trBlock);
                        string id = idMatch.Success ? idMatch.Groups[1].Value.Trim() : null;

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id) && !name.Contains("zip", StringComparison.OrdinalIgnoreCase) && seenIds.Add(id))
                        {
                            _logger.Info($"Ktuvit: Found subtitle - Name: {name}, ID: {id}");
                            subtitleDetails.Add(new KtuvitSubtitleDetails
                            {
                                Title = name,
                                SubtitleID = id
                            });
                        }
                    }
                }
                pos = endTr + 5; // Move past </tr>
            }
            return subtitleDetails;
        }
        public async Task<SubtitleResponse> DownloadSubtitle(string FilmID,  string SubtitleID)
        {
            var requestBody = new
            {
                request = new
                {
                    FilmID,
                    SubtitleID
                }
            };

            var json = _jsonSerializer.SerializeToString(requestBody);
            var httpRequest = new HttpRequestOptions();
            httpRequest.Url = RequestDownloadUrl;
            httpRequest.RequestContentType = "application/json";
            httpRequest.RequestContent = json.ToCharArray().AsMemory();
            var response = await _httpClient.Post(httpRequest);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                // First deserialization to get the wrapper response
                var initialResponse = _jsonSerializer.DeserializeFromStream<KtuvitDownloadRequestResponse>(response.Content);
                if (initialResponse?.d != null)
                {
                    // Second deserialization to parse the nested JSON string
                    var searchResults = _jsonSerializer.DeserializeFromString<KtuvitDownloadRequestResult>(initialResponse.d);
                    if (searchResults?.DownloadIdentifier != null)
                    {
                        string downloadId = searchResults.DownloadIdentifier;
                        var downloadRequest = new HttpRequestOptions();
                        downloadRequest.Url = $"{DownloadUrl}?DownloadIdentifier={downloadId}";
                        _logger.Info($"Ktuvit: Downloading subtitle file with Download ID: {downloadId}");
                        var downloadResponse = await _httpClient.SendAsync(downloadRequest, System.Net.Http.HttpMethod.Get.ToString());
                        MemoryStream fileData = new MemoryStream();
                        await downloadResponse.Content.CopyToAsync(fileData);
                        fileData.Position = 0L;
                        return new SubtitleResponse
                        {
                            Stream = fileData,
                            Format = "srt",
                            Language = "he"
                        };
                    }
                }
                else
                {
                    _logger.Warn($"Ktuvit API subtitle request failed: No data for ID: {SubtitleID}");
                    return null;
                }
            }
            else
            {
                _logger.Warn($"Ktuvit API subtitle request failed: {response.StatusCode} for ID: {SubtitleID}");
                return null;
            }
            return null;
        }
        
        public bool KtuvitAccessValidation()
        {
            try
            {
                var httpRequest = new HttpRequestOptions();
                httpRequest.Url = ApiBaseUrl;
                var requestTimeout = Plugin.Instance.Options.requestTimeout ?? 5;
                httpRequest.TimeoutMs = requestTimeout * 1000; // in milliseconds
                var responseTask = _httpClient.GetResponse(httpRequest);
                responseTask.Wait(); // Block until complete
                var response = responseTask.Result;
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.Info("Ktuvit: Access validation successful.");
                    return true;
                }
                else
                {
                    _logger.Error($"Ktuvit: Access validation failed with status code {response.StatusCode}. Ktuvit might not be available.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ktuvit: Access validation failed. Ktuvit might not be available: {ex}");
                return false;
            }
        }

        // Synchronous authentication for use in Validate
        public bool KtuvitAuthentication(string username, string password)
        {
            try
            {
                // Get Encryption Salt
                var httpRequest = new HttpRequestOptions();
                httpRequest.Url = ApiBaseUrl;
                var responseTask = _httpClient.GetResponse(httpRequest);
                responseTask.Wait(); // Block until complete
                var response = responseTask.Result;
                string htmlContent;
                using (var reader = new StreamReader(response.Content, Encoding.UTF8))
                {
                    htmlContent = reader.ReadToEnd();
                }
                var encryptionKeyMatch = Regex.Match(htmlContent, @"var encryptionSalt = '([A-Z0-9].+)'");
                string encryptionKey = encryptionKeyMatch.Success ? encryptionKeyMatch.Groups[1].Value : null;
                if (string.IsNullOrEmpty(encryptionKey))
                {
                    _logger.Error("Ktuvit: Failed to retrieve encryption salt.");
                    return false;
                }
                string encryptedPassword = CryptoCompat.EncryptKtuvitPassword(username, password, encryptionKey);
                if (string.IsNullOrEmpty(encryptedPassword))
                {
                    _logger.Error("Ktuvit: Password encryption failed.");
                    return false;
                }

                // Authenticate Ktuvit
                var loginBody = new
                {
                    request = new
                    {
                        Email = username,
                        Password = encryptedPassword
                    }
                };
                var json = _jsonSerializer.SerializeToString(loginBody);

                _logger.Info($"Ktuvit: Attempting login");
                httpRequest.Url = LoginUrl;
                httpRequest.RequestContentType = "application/json";
                httpRequest.RequestContent = json.ToCharArray().AsMemory();
                var loginResponse = _httpClient.Post(httpRequest);
                loginResponse.Wait(); // Block until complete
                var loginResult = loginResponse.Result;

                if (loginResponse.Result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // First deserialization to get the wrapper response
                    var initialResponse = _jsonSerializer.DeserializeFromStream<KtuvitLoginRequestResponse>(loginResult.Content);
                    if (initialResponse?.d != null)
                    {
                        // Second deserialization to parse the nested JSON string
                        var searchResults = _jsonSerializer.DeserializeFromString<KtuvitLoginRequestResult>(initialResponse.d);
                        if (searchResults?.IsSuccess == true)
                        {
                            _logger.Info("Ktuvit: Authentication successful.");
                            return true;
                        }
                        else
                        {
                            _logger.Error($"Ktuvit: Authentication failed: {searchResults?.ErrorMessage}");
                            return false;
                        }
                    }
                    else
                    {
                        _logger.Error("Ktuvit: Authentication failed: No data in response.");
                        return false;
                    }
                }
                else
                {
                    _logger.Error($"Ktuvit: Authentication failed with status code {loginResponse.Result.StatusCode}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Ktuvit: Authentication failed: {ex}");
                return false;
            }
        }
    }
}
