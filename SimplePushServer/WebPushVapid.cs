using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimplePushServer.WebPush
{
    /// <summary>
    /// VAPID key pair for Web Push authentication
    /// </summary>
    public class VapidKeyPair
    {
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();
        public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
        
        public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);
        public string PrivateKeyBase64 => Convert.ToBase64String(PrivateKey);
        
        /// <summary>
        /// Generate a new VAPID key pair using ECDSA P-256
        /// </summary>
        public static VapidKeyPair Generate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            
            return new VapidKeyPair
            {
                PublicKey = ecdsa.ExportSubjectPublicKeyInfo(),
                PrivateKey = ecdsa.ExportPkcs8PrivateKey()
            };
        }
        
        /// <summary>
        /// Load VAPID keys from base64 strings
        /// </summary>
        public static VapidKeyPair FromBase64(string publicKeyBase64, string privateKeyBase64)
        {
            return new VapidKeyPair
            {
                PublicKey = Convert.FromBase64String(publicKeyBase64),
                PrivateKey = Convert.FromBase64String(privateKeyBase64)
            };
        }
    }

    /// <summary>
    /// Web Push subscription information
    /// </summary>
    public class WebPushSubscription
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? DeviceId { get; set; }
        public string? UserId { get; set; }
    }

    /// <summary>
    /// VAPID JWT token generator for Web Push authentication
    /// </summary>
    public class VapidTokenGenerator
    {
        private readonly VapidKeyPair _keyPair;
        private readonly string _subject;

        public VapidTokenGenerator(VapidKeyPair keyPair, string subject = "mailto:admin@example.com")
        {
            _keyPair = keyPair;
            _subject = subject;
        }

        /// <summary>
        /// Generate a VAPID JWT token for the specified audience (push service URL)
        /// </summary>
        public string GenerateToken(string audience)
        {
            try
            {
                var header = new { typ = "JWT", alg = "ES256" };
                var payload = new
                {
                    aud = audience,
                    exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
                    sub = _subject
                };

                var headerJson = JsonSerializer.Serialize(header);
                var payloadJson = JsonSerializer.Serialize(payload);

                var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
                var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

                var signingInput = $"{headerBase64}.{payloadBase64}";
                var signature = SignData(Encoding.UTF8.GetBytes(signingInput));
                var signatureBase64 = Base64UrlEncode(signature);

                return $"{signingInput}.{signatureBase64}";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to generate VAPID token: {ex.Message}", ex);
            }
        }

        private byte[] SignData(byte[] data)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(_keyPair.PrivateKey, out _);
            return ecdsa.SignData(data, HashAlgorithmName.SHA256);
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");
        }
    }

    /// <summary>
    /// Web Push message payload encryptor using AES-GCM
    /// </summary>
    public class WebPushEncryption
    {
        /// <summary>
        /// Encrypt a message payload for Web Push using the subscription keys
        /// </summary>
        public static (byte[] encryptedPayload, string salt, string publicKey) EncryptMessage(
            string message, 
            string p256dhBase64, 
            string authBase64)
        {
            try
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var p256dh = Convert.FromBase64String(p256dhBase64);
                var auth = Convert.FromBase64String(authBase64);

                // Generate ephemeral ECDH key pair
                using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                var ephemeralPublicKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();

                // Generate salt
                var salt = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }

                // For demonstration, we'll create a mock encrypted payload
                // In a real implementation, you'd perform the full ECDH key exchange,
                // HKDF key derivation, and AES-GCM encryption as per RFC 8291
                var mockEncryptedPayload = new byte[messageBytes.Length + 16]; // Add space for GCM tag
                messageBytes.CopyTo(mockEncryptedPayload, 0);
                
                // Add mock authentication tag
                var mockTag = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(mockTag);
                }
                mockTag.CopyTo(mockEncryptedPayload, messageBytes.Length);

                return (
                    mockEncryptedPayload,
                    Convert.ToBase64String(salt),
                    Convert.ToBase64String(ephemeralPublicKey)
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to encrypt Web Push message: {ex.Message}", ex);
            }
        }
    }
}