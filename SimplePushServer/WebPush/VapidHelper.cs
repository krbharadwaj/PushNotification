using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimplePushServer.WebPush
{
    /// <summary>
    /// Helper class for VAPID key management and JWT generation
    /// </summary>
    public static class VapidHelper
    {
        /// <summary>
        /// Extract public key from private key and format for Web Push
        /// </summary>
        public static (string PublicKeyBase64Url, ECDsa EcdsaKey) ExtractPublicKeyFromPrivateKey(string privateKeyBase64)
        {
            try
            {
                // Decode the Base64 private key
                var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);
                
                // Import the PKCS8 private key
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                
                // Export the public key in UNCOMPRESSED format (required for Web Push VAPID)
                // This returns 65 bytes: 0x04 + 32 bytes X + 32 bytes Y
                var publicKeyParams = ecdsa.ExportParameters(false);
                var uncompressedPublicKey = new byte[65];
                uncompressedPublicKey[0] = 0x04; // Uncompressed point indicator
                Array.Copy(publicKeyParams.Q.X!, 0, uncompressedPublicKey, 1, 32);
                Array.Copy(publicKeyParams.Q.Y!, 0, uncompressedPublicKey, 33, 32);
                
                // Convert to Base64 URL-safe encoding (required for VAPID)
                var publicKeyBase64Url = Convert.ToBase64String(uncompressedPublicKey)
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .TrimEnd('=');
                
                // Return both the formatted public key and a new ECDsa instance for JWT signing
                var signingKey = ECDsa.Create();
                signingKey.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                
                return (publicKeyBase64Url, signingKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract public key from private key: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generate VAPID JWT token using the device's private key
        /// </summary>
        public static string GenerateVapidJwt(string audience, string subject, ECDsa privateKey)
        {
            try
            {
                // JWT Header
                var header = new
                {
                    typ = "JWT",
                    alg = "ES256"
                };

                // JWT Payload
                var payload = new
                {
                    aud = audience,
                    exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(), // 12 hours expiry
                    sub = subject
                };

                // Encode header and payload
                var headerJson = JsonSerializer.Serialize(header);
                var payloadJson = JsonSerializer.Serialize(payload);

                var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
                var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

                // Create signature data
                var signatureData = $"{headerBase64}.{payloadBase64}";
                var signatureBytes = Encoding.UTF8.GetBytes(signatureData);

                // Sign with ES256 (ECDSA using P-256 and SHA-256)
                var signature = privateKey.SignData(signatureBytes, HashAlgorithmName.SHA256);
                var signatureBase64 = Base64UrlEncode(signature);

                // Return complete JWT
                return $"{signatureData}.{signatureBase64}";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to generate VAPID JWT: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Base64 URL-safe encoding (as required by JWT and VAPID specifications)
        /// </summary>
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// Extract audience (origin) from channel URI for VAPID JWT
        /// </summary>
        public static string GetAudienceFromChannelUri(string channelUri)
        {
            try
            {
                var uri = new Uri(channelUri);
                return $"{uri.Scheme}://{uri.Host}";
            }
            catch
            {
                // Fallback for Web Push endpoints
                return "https://notify.windows.com";
            }
        }
    }
}