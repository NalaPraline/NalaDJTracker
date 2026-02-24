using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Nala.Models;

namespace Nala;

public class TwitchService : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly IPluginLog log;
    private string? accessToken;
    private DateTime tokenExpiry = DateTime.MinValue;

    public TwitchService(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private async Task<bool> EnsureAccessTokenAsync(string clientId, string clientSecret)
    {
        if (!string.IsNullOrEmpty(accessToken) && DateTime.UtcNow < tokenExpiry)
        {
            return true;
        }

        try
        {
            var tokenUrl = "https://id.twitch.tv/oauth2/token";
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await httpClient.PostAsync(tokenUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                log.Error($"Failed to get Twitch access token: {response.StatusCode}");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // Refresh 1 minute early

            log.Information("Twitch access token obtained successfully");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Error getting Twitch access token: {ex.Message}");
            return false;
        }
    }

    public async Task<List<DjStreamer>> GetStreamsAsync(string clientId, string clientSecret, List<string> usernames)
    {
        var streamers = new List<DjStreamer>();

        if (usernames.Count == 0)
        {
            return streamers;
        }

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            log.Warning("Twitch Client ID or Client Secret not configured");
            return streamers;
        }

        if (!await EnsureAccessTokenAsync(clientId, clientSecret))
        {
            return streamers;
        }

        try
        {
            // Build the query string with multiple user_login parameters
            var queryParams = string.Join("&", usernames.Select(u => $"user_login={Uri.EscapeDataString(u.ToLower())}"));
            var url = $"https://api.twitch.tv/helix/streams?{queryParams}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Client-ID", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                log.Error($"Failed to get streams: {response.StatusCode}");
                // If unauthorized, clear token to force refresh
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    accessToken = null;
                    tokenExpiry = DateTime.MinValue;
                }
                return streamers;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            // Create a dictionary of live streams by username
            var liveStreams = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var stream in data.EnumerateArray())
            {
                var userLogin = stream.GetProperty("user_login").GetString()?.ToLower() ?? "";
                liveStreams[userLogin] = stream;
            }

            // Create DjStreamer objects for all usernames
            foreach (var username in usernames)
            {
                var streamer = new DjStreamer
                {
                    TwitchUsername = username.ToLower()
                };

                if (liveStreams.TryGetValue(username.ToLower(), out var stream))
                {
                    streamer.IsLive = true;
                    streamer.DisplayName = stream.GetProperty("user_name").GetString() ?? username;
                    streamer.StreamTitle = stream.GetProperty("title").GetString() ?? "";
                    streamer.ViewerCount = stream.GetProperty("viewer_count").GetInt32();
                    streamer.GameName = stream.GetProperty("game_name").GetString() ?? "";
                    streamer.ThumbnailUrl = stream.GetProperty("thumbnail_url").GetString() ?? "";

                    if (stream.TryGetProperty("started_at", out var startedAt))
                    {
                        var startedAtStr = startedAt.GetString();
                        if (!string.IsNullOrEmpty(startedAtStr) && DateTime.TryParse(startedAtStr, out var parsedDate))
                        {
                            streamer.StartedAt = parsedDate;
                        }
                    }
                }
                else
                {
                    streamer.IsLive = false;
                    streamer.DisplayName = username;
                }

                streamers.Add(streamer);
            }

            return streamers;
        }
        catch (Exception ex)
        {
            log.Error($"Error fetching streams: {ex.Message}");
            return streamers;
        }
    }

    public void InvalidateToken()
    {
        accessToken = null;
        tokenExpiry = DateTime.MinValue;
    }
}
