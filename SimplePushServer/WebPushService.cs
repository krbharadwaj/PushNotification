using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SimplePushServer.WebPush;

namespace SimplePushServer.Services
{
    /// <summary>
    /// Web Push service for sending VAPID-authenticated push notifications
    /// </summary>
    public class WebPushService
    {
        private readonly HttpClient _httpClient;
        private readonly VapidKeyPair _vapidKeys;
        private readonly VapidTokenGenerator _tokenGenerator;
        private readonly ILogger<WebPushService>? _logger;

        public WebPushService(HttpClient httpClient, VapidKeyPair vapidKeys, ILogger<WebPushService>? logger = null)
        {
            _httpClient = httpClient;
            _vapidKeys = vapidKeys;
            _tokenGenerator = new VapidTokenGenerator(vapidKeys);
            _logger = logger;
        }

        /// <summary>
        /// Send a Web Push notification using VAPID authentication
        /// </summary>
        public async Task<WebPushResult> SendNotificationAsync(
            WebPushSubscription subscription,
            string message,
            string? title = null,
            int ttl = 3600)
        {
            try
            {
                _logger?.LogInformation("Sending Web Push notification to {Endpoint}", subscription.Endpoint);

                // Create payload
                var payload = JsonSerializer.Serialize(new
                {
                    title = title ?? "Web Push Notification",
                    body = message,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    data = new { message, title }
                });

                // Generate VAPID token
                var audience = GetAudience(subscription.Endpoint);
                var vapidToken = _tokenGenerator.GenerateToken(audience);

                // Encrypt payload (simplified for demo - real implementation would use proper encryption)
                var (encryptedPayload, salt, publicKey) = WebPushEncryption.EncryptMessage(
                    payload, 
                    subscription.P256dh, 
                    subscription.Auth
                );

                // Create HTTP request
                var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint);
                
                // Add VAPID headers
                request.Headers.Authorization = new AuthenticationHeaderValue("vapid", 
                    $"t={vapidToken}, k={_vapidKeys.PublicKeyBase64}");
                
                // Add Web Push headers
                request.Headers.Add("TTL", ttl.ToString());
                request.Headers.Add("Urgency", "normal");
                
                // Add encryption headers (for encrypted payloads)
                if (encryptedPayload.Length > 0)
                {
                    request.Headers.Add("Content-Encoding", "aes128gcm");
                    request.Headers.Add("Crypto-Key", $"dh={publicKey}; p256ecdsa={_vapidKeys.PublicKeyBase64}");
                    request.Headers.Add("Encryption", $"salt={salt}");
                }

                // Set content
                request.Content = new ByteArrayContent(encryptedPayload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // Send request
                var response = await _httpClient.SendAsync(request);

                var result = new WebPushResult
                {
                    Success = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase ?? string.Empty,
                    Endpoint = subscription.Endpoint,
                    SentAt = DateTime.UtcNow
                };

                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = await response.Content.ReadAsStringAsync();
                    _logger?.LogWarning("Web Push failed: {StatusCode} - {Error}", 
                        response.StatusCode, result.ErrorMessage);
                }
                else
                {
                    _logger?.LogInformation("Web Push sent successfully: {StatusCode}", response.StatusCode);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception sending Web Push notification");
                return new WebPushResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Endpoint = subscription.Endpoint,
                    SentAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Send Web Push notification to multiple subscriptions
        /// </summary>
        public async Task<List<WebPushResult>> SendBulkNotificationAsync(
            IEnumerable<WebPushSubscription> subscriptions,
            string message,
            string? title = null,
            int ttl = 3600)
        {
            var tasks = subscriptions.Select(sub => 
                SendNotificationAsync(sub, message, title, ttl));
            
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        /// <summary>
        /// Validate a Web Push subscription by sending a test message
        /// </summary>
        public async Task<bool> ValidateSubscriptionAsync(WebPushSubscription subscription)
        {
            try
            {
                // Send a minimal test payload
                var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint);
                
                var audience = GetAudience(subscription.Endpoint);
                var vapidToken = _tokenGenerator.GenerateToken(audience);
                
                request.Headers.Authorization = new AuthenticationHeaderValue("vapid", 
                    $"t={vapidToken}, k={_vapidKeys.PublicKeyBase64}");
                request.Headers.Add("TTL", "0");
                
                // Empty payload for validation
                request.Content = new ByteArrayContent(Array.Empty<byte>());

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception validating subscription");
                return false;
            }
        }

        /// <summary>
        /// Extract audience (origin) from push service endpoint
        /// </summary>
        private static string GetAudience(string endpoint)
        {
            var uri = new Uri(endpoint);
            return $"{uri.Scheme}://{uri.Host}";
        }

        /// <summary>
        /// Get VAPID public key for client registration
        /// </summary>
        public string GetPublicKey() => _vapidKeys.PublicKeyBase64;
    }

    /// <summary>
    /// Result of a Web Push operation
    /// </summary>
    public class WebPushResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string ReasonPhrase { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    /// <summary>
    /// Web Push message request
    /// </summary>
    public record WebPushMessageRequest(
        string DeviceId,
        string Message,
        string? Title = null,
        int Ttl = 3600
    );

    /// <summary>
    /// Web Push subscription registration request
    /// </summary>
    public record WebPushSubscriptionRequest(
        string DeviceId,
        string UserId,
        string Endpoint,
        string P256dh,
        string Auth
    );
}