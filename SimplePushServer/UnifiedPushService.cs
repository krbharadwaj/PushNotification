using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SimplePushServer.Services
{
    /// <summary>
    /// Unified Push Service supporting both WNS and VAPID
    /// </summary>
    public class UnifiedPushService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<UnifiedPushService>? _logger;

        public UnifiedPushService(HttpClient httpClient, ILogger<UnifiedPushService>? logger = null)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Send VAPID push notification (simplified version)
        /// </summary>
        public async Task<PushResult> SendVapidNotificationAsync(
            string channelUri,
            string privateKey,
            string message,
            string? title = null)
        {
            try
            {
                _logger?.LogInformation("Sending VAPID notification to {ChannelUri}", channelUri);

                var payload = JsonSerializer.Serialize(new
                {
                    title = title ?? "VAPID Notification",
                    message = message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "vapid"
                });

                var request = new HttpRequestMessage(HttpMethod.Post, channelUri);
                
                // Simplified VAPID authentication (in production, use proper JWT VAPID tokens)
                var keyHash = Convert.ToBase64String(Convert.FromBase64String(privateKey).Take(32).ToArray());
                request.Headers.Add("X-VAPID-Key", keyHash);
                request.Headers.Add("Content-Type", "application/json");
                
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                return new PushResult
                {
                    Success = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    Message = response.IsSuccessStatusCode 
                        ? "VAPID notification sent successfully" 
                        : $"VAPID notification failed: {response.StatusCode}",
                    Type = "VAPID",
                    SentAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception sending VAPID notification");
                return new PushResult
                {
                    Success = false,
                    StatusCode = 0,
                    Message = $"VAPID notification error: {ex.Message}",
                    Type = "VAPID",
                    SentAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Send WNS push notification
        /// </summary>
        public async Task<PushResult> SendWnsNotificationAsync(
            string channelUri,
            string accessToken,
            string message,
            string? title = null)
        {
            try
            {
                _logger?.LogInformation("Sending WNS notification to {ChannelUri}", channelUri);

                var payload = JsonSerializer.Serialize(new
                {
                    title = title ?? "WNS Notification",
                    message = message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "wns"
                });

                var request = new HttpRequestMessage(HttpMethod.Post, channelUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Add("X-WNS-Type", "wns/raw");
                request.Headers.Add("X-WNS-RequestForStatus", "true");
                
                request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var response = await _httpClient.SendAsync(request);

                return new PushResult
                {
                    Success = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    Message = response.IsSuccessStatusCode 
                        ? "WNS notification sent successfully" 
                        : $"WNS notification failed: {response.StatusCode}",
                    Type = "WNS",
                    SentAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception sending WNS notification");
                return new PushResult
                {
                    Success = false,
                    StatusCode = 0,
                    Message = $"WNS notification error: {ex.Message}",
                    Type = "WNS",
                    SentAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Get WNS access token from Azure AD
        /// </summary>
        public async Task<string?> GetWnsAccessTokenAsync(string tenantId, string clientId, string clientSecret)
        {
            try
            {
                var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
                
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                    new KeyValuePair<string, string>("scope", "https://wns.windows.com/.default")
                });

                var response = await _httpClient.PostAsync(tokenEndpoint, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var tokenDoc = JsonDocument.Parse(responseJson);
                    return tokenDoc.RootElement.GetProperty("access_token").GetString();
                }
                
                _logger?.LogWarning("WNS token request failed: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting WNS access token");
                return null;
            }
        }
    }

    /// <summary>
    /// Result of a push notification operation
    /// </summary>
    public class PushResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "WNS" or "VAPID"
        public DateTime SentAt { get; set; }
    }
}