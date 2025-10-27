using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Networking.PushNotifications;
using Windows.Security.Cryptography;

namespace WinUI3AppForWNSTest
{
    /// <summary>
    /// Simple VAPID key pair
    /// </summary>
    public class VapidKeyPair
    {
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();
        public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
        
        public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);
        public string PrivateKeyBase64 => Convert.ToBase64String(PrivateKey);
    }

    /// <summary>
    /// Simple Web Push Manager - Two step process: Generate Keys -> Subscribe
    /// </summary>
    public static class WebPushManager
    {
        // Events
        public static event Action<string>? StatusUpdated;

        // Private fields
        private static VapidKeyPair? _vapidKeys;
        private static string? _channelUri;

        /// <summary>
        /// Current VAPID keys (if generated)
        /// </summary>
        public static VapidKeyPair? VapidKeys => _vapidKeys;

        /// <summary>
        /// Current channel URI (if subscribed)
        /// </summary>
        public static string? ChannelUri => _channelUri;

        /// <summary>
        /// Step 1: Generate VAPID Key Pair
        /// </summary>
        public static bool GenerateVapidKeys()
        {
            try
            {
                StatusUpdated?.Invoke("🔄 Generating VAPID key pair...");

                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                
                _vapidKeys = new VapidKeyPair
                {
                    PublicKey = ecdsa.ExportSubjectPublicKeyInfo(),
                    PrivateKey = ecdsa.ExportPkcs8PrivateKey()
                };

                StatusUpdated?.Invoke("✅ VAPID key pair generated!");
                StatusUpdated?.Invoke($"🔑 Public key: {_vapidKeys.PublicKeyBase64[..32]}...");
                StatusUpdated?.Invoke($"🔐 Private key: {_vapidKeys.PrivateKeyBase64[..32]}...");
                StatusUpdated?.Invoke("💡 Ready to subscribe with these keys");

                return true;
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Key generation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Step 2: Subscribe and send to server
        /// </summary>
        public static async Task<bool> SubscribeAsync(string serverUrl = "http://localhost:5000")
        {
            try
            {
                if (_vapidKeys == null)
                {
                    StatusUpdated?.Invoke("❌ Generate VAPID keys first!");
                    return false;
                }

                StatusUpdated?.Invoke("📡 Creating push notification channel with VAPID public key...");

                // Convert public key to uncompressed format for channel creation
                using var tempEcdsa = ECDsa.Create();
                tempEcdsa.ImportPkcs8PrivateKey(_vapidKeys.PrivateKey, out _);
                var publicKeyParams = tempEcdsa.ExportParameters(false);
                
                // Create uncompressed public key (65 bytes: 0x04 + 32 bytes X + 32 bytes Y)
                var uncompressedPublicKey = new byte[65];
                uncompressedPublicKey[0] = 0x04;
                Array.Copy(publicKeyParams.Q.X!, 0, uncompressedPublicKey, 1, 32);
                Array.Copy(publicKeyParams.Q.Y!, 0, uncompressedPublicKey, 33, 32);
                
                var keyBuffer = CryptographicBuffer.CreateFromByteArray(uncompressedPublicKey);
                var appServerKeyId = Convert.ToBase64String(uncompressedPublicKey.Take(16).ToArray());
                
                StatusUpdated?.Invoke($"🔑 Using VAPID public key for channel: {Convert.ToBase64String(uncompressedPublicKey)[..20]}...");
                
                var channelManager = PushNotificationChannelManager.GetDefault();
                var channel = await channelManager.CreateRawPushNotificationChannelWithAlternateKeyForApplicationAsync(
                    keyBuffer, 
                    appServerKeyId
                );

                if (channel == null)
                {
                    StatusUpdated?.Invoke("❌ Failed to create push notification channel");
                    return false;
                }

                _channelUri = channel.Uri;
                StatusUpdated?.Invoke("✅ Web Push channel created with VAPID keys!");
                StatusUpdated?.Invoke($"📡 Channel URI: {_channelUri}");

                // Send to SimplePushServer
                return await SendToServerAsync(serverUrl);
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Subscribe failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send channel URI and private key to SimplePushServer
        /// </summary>
        private static async Task<bool> SendToServerAsync(string serverUrl)
        {
            try
            {
                StatusUpdated?.Invoke($"📤 Sending to server: {serverUrl}");

                using var httpClient = new HttpClient();
                
                var deviceId = Environment.MachineName + "_" + Guid.NewGuid().ToString("N")[..8];
                
                var data = new
                {
                    DeviceId = deviceId,
                    ChannelUri = _channelUri,
                    PrivateKey = _vapidKeys!.PrivateKeyBase64
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{serverUrl}/subscribe", content);
                
                if (response.IsSuccessStatusCode)
                {
                    StatusUpdated?.Invoke("✅ Successfully sent to SimplePushServer!");
                    StatusUpdated?.Invoke("🔔 Server can now send push messages");
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    StatusUpdated?.Invoke($"❌ Server failed: {response.StatusCode} - {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Server error: {ex.Message}");
                return false;
            }
        }
    }
}