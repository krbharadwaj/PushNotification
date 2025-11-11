using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Windows.PushNotifications;
using Microsoft.Windows.AppLifecycle;
using Windows.Foundation;
using System.Diagnostics;

namespace WinUI3AppForWNSTest
{
    /// <summary>
    /// Push notification manager following Microsoft's official Windows App SDK implementation pattern
    /// Based on: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/push-notifications/push-quickstart
    /// </summary>
    public class PushManager
    {
        // Azure AD Application Registration Details - Loaded from SECRETS.config
        // IMPORTANT: This uses the Object ID from the Enterprise Application (Service Principal), NOT the App Registration Object ID
        private static readonly Lazy<Dictionary<string, string>> _secrets = new(() => LoadSecrets());
        
        private static readonly Guid AzureObjectId = GetGuidFromSecrets("AzureObjectId", "fb750b7b-c2b6-4106-b556-b9b1fac4d1f6");
        private static readonly Guid AzureAppId = GetGuidFromSecrets("ClientId", "9c959ee1-3eb8-4cfa-a528-4a04331dbdd9");
        private static readonly string TenantId = _secrets.Value.GetValueOrDefault("TenantId") ?? "YOUR_TENANT_ID_HERE";
        private static readonly string ClientSecret = _secrets.Value.GetValueOrDefault("ClientSecret") ?? "YOUR_CLIENT_SECRET_HERE";

        /// <summary>
        /// Safely get GUID from secrets with fallback
        /// </summary>
        private static Guid GetGuidFromSecrets(string key, string fallbackValue)
        {
            try
            {
                var value = _secrets.Value.GetValueOrDefault(key);
                if (!string.IsNullOrEmpty(value) && value != $"YOUR_{key.ToUpper()}_HERE")
                {
                    return new Guid(value);
                }
                return new Guid(fallbackValue);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing GUID for {key}: {ex.Message}, using fallback");
                return new Guid(fallbackValue);
            }
        }

        /// <summary>
        /// Load secrets from SECRETS.config file
        /// </summary>
        private static Dictionary<string, string> LoadSecrets()
        {
            var secrets = new Dictionary<string, string>();
            
            // Try multiple possible locations for SECRETS.config
            var possiblePaths = new[]
            {
                // Project root (most likely for development)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "SECRETS.config"),
                // Current directory
                Path.Combine(Directory.GetCurrentDirectory(), "SECRETS.config"),
                // One level up from current directory  
                Path.Combine(Directory.GetCurrentDirectory(), "..", "SECRETS.config"),
                // Solution root (assuming we're in a subdirectory)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "SECRETS.config"),
                // AppX package directory (for packaged apps)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "WinUI3AppForWNSTest", "SECRETS.config")
            };
            
            string? foundPath = null;
            foreach (var path in possiblePaths)
            {
                try
                {
                    var normalizedPath = Path.GetFullPath(path);
                    if (File.Exists(normalizedPath))
                    {
                        foundPath = normalizedPath;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking path {path}: {ex.Message}");
                }
            }
            
            if (foundPath != null)
            {
                try
                {
                    foreach (var line in File.ReadAllLines(foundPath))
                    {
                        if (!string.IsNullOrWhiteSpace(line) && line.Contains('='))
                        {
                            var parts = line.Split('=', 2);
                            if (parts.Length == 2)
                            {
                                secrets[parts[0].Trim()] = parts[1].Trim();
                            }
                        }
                    }
                    Debug.WriteLine($"✅ Loaded {secrets.Count} secrets from: {foundPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error reading SECRETS.config: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine($"⚠️ SECRETS.config not found in any of the expected locations");
                Debug.WriteLine("Searched paths:");
                foreach (var path in possiblePaths)
                {
                    try
                    {
                        Debug.WriteLine($"  - {Path.GetFullPath(path)}");
                    }
                    catch
                    {
                        Debug.WriteLine($"  - {path} (invalid path)");
                    }
                }
            }
            return secrets;
        }

        // Events for communication with UI
        public static event Action<string>? StatusUpdated;
        public static event Action<string>? NotificationReceived;
        public static event Action<string, DateTimeOffset>? ChannelReceived;

        private static PushNotificationChannel? _currentChannel;
        private static bool _isRegistered = false;

        /// <summary>
        /// Initialize push notifications following the official Microsoft pattern
        /// </summary>
        public static async Task<bool> InitializeAsync()
        {
            try
            {
                StatusUpdated?.Invoke("🔄 Initializing push notifications...");

                // Step 1: Register event handlers BEFORE calling Register() - Critical!
                if (!_isRegistered)
                {
                    StatusUpdated?.Invoke("🔄 Registering event handlers...");
                    PushNotificationManager.Default.PushReceived += OnPushReceived;
                    
                    // Step 2: Register the app for push notifications
                    StatusUpdated?.Invoke("🔄 Registering for push notifications...");
                    PushNotificationManager.Default.Register();
                    _isRegistered = true;
                }

                // Step 3: Check if push notifications are supported
                if (!PushNotificationManager.IsSupported())
                {
                    StatusUpdated?.Invoke("❌ Push notifications are NOT supported on this device");
                    
                    // Check if this is a packaging issue
                    bool isPackaged = await CheckIfAppIsPackaged();
                    if (!isPackaged)
                    {
                        StatusUpdated?.Invoke("💡 App appears to be unpackaged. Consider packaging as MSIX for full push support.");
                    }
                    return false;
                }

                StatusUpdated?.Invoke("✅ Push notifications are supported");

                // Step 4: Handle activation arguments (background push activation)
                await HandleActivationAsync();

                // Step 5: Request a WNS Channel URI
                StatusUpdated?.Invoke("🔄 Requesting WNS Channel...");
                _currentChannel = await RequestChannelAsync();
                
                if (_currentChannel != null)
                {
                    StatusUpdated?.Invoke("✅ Push notification initialization completed successfully!");
                    ChannelReceived?.Invoke(_currentChannel.Uri.ToString(), _currentChannel.ExpirationTime);
                    return true;
                }
                else
                {
                    StatusUpdated?.Invoke("❌ Failed to obtain WNS Channel");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeAsync exception: {ex}");
                StatusUpdated?.Invoke($"❌ Initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Request a WNS Channel URI with proper async handling and progress tracking
        /// </summary>
        private static async Task<PushNotificationChannel?> RequestChannelAsync()
        {
            try
            {
                // Create the channel request with your Azure Object ID
                var channelOperation = PushNotificationManager.Default.CreateChannelAsync(AzureObjectId);

                // Wait for the channel creation to complete
                var result = await channelOperation;

                // Handle the result
                switch (result.Status)
                {
                    case PushNotificationChannelStatus.CompletedSuccess:
                        var channel = result.Channel;
                        StatusUpdated?.Invoke($"✅ Channel created successfully!");
                        StatusUpdated?.Invoke($"📋 Channel URI: {channel.Uri}");
                        StatusUpdated?.Invoke($"⏰ Expires: {channel.ExpirationTime:yyyy-MM-dd HH:mm:ss}");
                        
                        // Important: Store this channel URI and send it to your backend server
                        // The backend will use this URI to send push notifications to this specific app instance
                        Debug.WriteLine($"Channel URI: {channel.Uri}");
                        Debug.WriteLine($"Channel Expiry: {channel.ExpirationTime}");
                        
                        return channel;

                    case PushNotificationChannelStatus.CompletedFailure:
                        StatusUpdated?.Invoke($"❌ Channel creation failed: {result.ExtendedError?.Message}");
                        Debug.WriteLine($"Channel creation failed with error: {result.ExtendedError}");
                        return null;

                    default:
                        StatusUpdated?.Invoke($"❌ Unexpected channel creation result: {result.Status}");
                        Debug.WriteLine($"Unexpected channel result status: {result.Status}");
                        return null;
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Channel creation exception: {ex.Message}");
                Debug.WriteLine($"RequestChannelAsync exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Handle foreground push notification events
        /// </summary>
        private static void OnPushReceived(PushNotificationManager sender, PushNotificationReceivedEventArgs args)
        {
            try
            {
                // Extract the payload from the push notification
                var payloadBytes = args.Payload;
                var payloadString = Encoding.UTF8.GetString(payloadBytes);
                
                // Immediate notification that push was received
                Console.WriteLine($"\n🔔 === PUSH NOTIFICATION RECEIVED ===");
                StatusUpdated?.Invoke($"🔔 PUSH NOTIFICATION RECEIVED!");
                
                // Enhanced logging for push notification received
                Debug.WriteLine($"🔔 PUSH NOTIFICATION RECEIVED (FOREGROUND)");
                Debug.WriteLine($"📅 Received at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                Debug.WriteLine($"📦 Raw payload: {payloadString}");
                Debug.WriteLine($"📏 Payload size: {payloadBytes.Length} bytes");
                
                // Try to parse the JSON payload for better logging
                string messageText = "Unknown message";
                string titleText = "No title";
                
                try
                {
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(payloadString);
                    if (jsonDoc.RootElement.TryGetProperty("message", out var msgElement))
                    {
                        messageText = msgElement.GetString() ?? "Empty message";
                    }
                    if (jsonDoc.RootElement.TryGetProperty("title", out var titleElement))
                    {
                        titleText = titleElement.GetString() ?? "No title";
                    }
                }
                catch
                {
                    // If JSON parsing fails, use the raw string as message
                    messageText = payloadString;
                }
                
                // User-friendly status updates
                StatusUpdated?.Invoke($"🔔 PUSH NOTIFICATION RECEIVED!");
                StatusUpdated?.Invoke($"📋 Title: {titleText}");
                StatusUpdated?.Invoke($"� Message: {messageText}");
                StatusUpdated?.Invoke($"⏰ Received: {DateTime.Now:HH:mm:ss}");
                StatusUpdated?.Invoke($"📦 Full payload: {payloadString}");
                
                // Console output for easy debugging
                Console.WriteLine($"\n🔔 === PUSH NOTIFICATION RECEIVED ===");
                Console.WriteLine($"Title: {titleText}");
                Console.WriteLine($"Message: {messageText}");
                Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Raw: {payloadString}");
                Console.WriteLine($"=======================================\n");
                
                // Show toast notification with actual message
                ShowToastSafely(titleText, messageText);
                
                // Notify subscribers about the received notification
                NotificationReceived?.Invoke(payloadString);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ OnPushReceived exception: {ex}");
                StatusUpdated?.Invoke($"❌ Error processing push notification: {ex.Message}");
                Console.WriteLine($"❌ Push notification error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle app activation scenarios, including background push activation
        /// </summary>
        private static async Task HandleActivationAsync()
        {
            try
            {
                var args = AppInstance.GetCurrent().GetActivatedEventArgs();
                
                switch (args.Kind)
                {
                    case ExtendedActivationKind.Launch:
                        StatusUpdated?.Invoke("📱 App launched normally");
                        break;

                    case ExtendedActivationKind.Push:
                        StatusUpdated?.Invoke("� APP ACTIVATED BY PUSH NOTIFICATION!");
                        
                        // Handle background push activation
                        if (args.Data is PushNotificationReceivedEventArgs pushArgs)
                        {
                            // Get deferral to ensure proper background processing
                            var deferral = pushArgs.GetDeferral();
                            
                            try
                            {
                                // Process the background push notification
                                var payload = pushArgs.Payload;
                                var payloadString = Encoding.UTF8.GetString(payload);
                                
                                // Enhanced logging for background activation
                                Debug.WriteLine($"🚀 BACKGROUND PUSH ACTIVATION");
                                Debug.WriteLine($"📅 Activated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                                Debug.WriteLine($"📦 Payload: {payloadString}");
                                
                                // Try to parse the JSON payload for better logging
                                string messageText = "Unknown message";
                                string titleText = "No title";
                                
                                try
                                {
                                    var jsonDoc = System.Text.Json.JsonDocument.Parse(payloadString);
                                    if (jsonDoc.RootElement.TryGetProperty("message", out var msgElement))
                                    {
                                        messageText = msgElement.GetString() ?? "Empty message";
                                    }
                                    if (jsonDoc.RootElement.TryGetProperty("title", out var titleElement))
                                    {
                                        titleText = titleElement.GetString() ?? "No title";
                                    }
                                }
                                catch
                                {
                                    messageText = payloadString;
                                }
                                
                                // User-friendly status updates
                                StatusUpdated?.Invoke($"🚀 BACKGROUND ACTIVATION SUCCESSFUL!");
                                StatusUpdated?.Invoke($"� Push Title: {titleText}");
                                StatusUpdated?.Invoke($"💬 Push Message: {messageText}");
                                StatusUpdated?.Invoke($"⏰ Activated at: {DateTime.Now:HH:mm:ss}");
                                StatusUpdated?.Invoke($"📦 Full payload: {payloadString}");
                                
                                // Console output for debugging
                                Console.WriteLine($"\n🚀 === BACKGROUND PUSH ACTIVATION ===");
                                Console.WriteLine($"Title: {titleText}");
                                Console.WriteLine($"Message: {messageText}");
                                Console.WriteLine($"Activated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                Console.WriteLine($"Raw: {payloadString}");
                                Console.WriteLine($"====================================\n");
                                
                                NotificationReceived?.Invoke(payloadString);
                                
                                // Perform any necessary background work here
                                await Task.Delay(100); // Simulate processing
                            }
                            finally
                            {
                                // Always complete the deferral
                                deferral.Complete();
                            }
                        }
                        break;

                    default:
                        StatusUpdated?.Invoke($"📱 App activated with kind: {args.Kind}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleActivationAsync exception: {ex}");
                StatusUpdated?.Invoke($"❌ Error handling activation: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the app is packaged (has package identity)
        /// </summary>
        private static Task<bool> CheckIfAppIsPackaged()
        {
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                StatusUpdated?.Invoke($"📦 App is packaged: {package.DisplayName}");
                return Task.FromResult(true);
            }
            catch
            {
                StatusUpdated?.Invoke("📦 App is unpackaged");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Request access token for WNS (typically used by backend server)
        /// </summary>
        public static async Task<string?> RequestAccessTokenAsync()
        {
            try
            {
                StatusUpdated?.Invoke("🔑 Testing token request (using placeholder credentials)...");
                
                using var client = new System.Net.Http.HttpClient();
                var tokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
                
                var content = new System.Net.Http.FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", AzureAppId.ToString()),
                    new KeyValuePair<string, string>("client_secret", ClientSecret),
                    new KeyValuePair<string, string>("scope", "https://wns.windows.com/.default")
                });

                var response = await client.PostAsync(tokenEndpoint, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    // Note: Token failures are expected when using placeholder credentials
                    Debug.WriteLine($"Token request failed: {response.StatusCode} - {responseJson}");
                    return null;
                }

                // Parse the access token from the response
                var tokenDoc = System.Text.Json.JsonDocument.Parse(responseJson);
                if (tokenDoc.RootElement.TryGetProperty("access_token", out var tokenElement))
                {
                    var token = tokenElement.GetString();
                    StatusUpdated?.Invoke("✅ Access token obtained successfully");
                    return token;
                }
                
                Debug.WriteLine("Access token not found in response");
                return null;
            }
            catch (Exception ex)
            {
                // Note: Token exceptions are expected when using placeholder credentials
                Debug.WriteLine($"RequestAccessTokenAsync exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Send a raw push notification (typically called from backend server)
        /// </summary>
        public static async Task<bool> SendRawPushNotificationAsync(string channelUri, string accessToken, string payload)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrEmpty(channelUri) || !Uri.IsWellFormedUriString(channelUri, UriKind.Absolute))
                {
                    StatusUpdated?.Invoke("❌ Invalid channel URI");
                    return false;
                }
                
                if (string.IsNullOrEmpty(accessToken))
                {
                    StatusUpdated?.Invoke("❌ Invalid access token");
                    return false;
                }
                
                StatusUpdated?.Invoke("📤 Sending push notification...");
                StatusUpdated?.Invoke($"📋 Channel: {channelUri.Substring(0, Math.Min(50, channelUri.Length))}...");
                
                using var client = new System.Net.Http.HttpClient();
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, channelUri);
                
                // Set required headers for raw push notification
                request.Headers.Add("Authorization", $"Bearer {accessToken}");
                request.Headers.Add("X-WNS-Type", "wns/raw");
                request.Headers.Add("X-WNS-RequestForStatus", "true");
                
                // Set the payload with proper content type for raw notifications
                request.Content = new System.Net.Http.ByteArrayContent(Encoding.UTF8.GetBytes(payload));
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                
                var response = await client.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    // Check WNS response headers for additional info
                    var deviceConnectionStatus = response.Headers.GetValues("X-WNS-DeviceConnectionStatus").FirstOrDefault();
                    var notificationStatus = response.Headers.GetValues("X-WNS-NotificationStatus").FirstOrDefault();
                    
                    StatusUpdated?.Invoke($"✅ Push sent successfully: {response.StatusCode}");
                    StatusUpdated?.Invoke($"📱 Device Status: {deviceConnectionStatus}, Notification: {notificationStatus}");
                    Debug.WriteLine($"Push notification sent successfully: {response.StatusCode}");
                    Debug.WriteLine($"Device Status: {deviceConnectionStatus}, Notification Status: {notificationStatus}");
                    return true;
                }
                else
                {
                    // Get detailed error information from WNS response headers
                    var errorDescription = "Unknown error";
                    var errorDetails = "No details available";
                    
                    try
                    {
                        if (response.Headers.Contains("X-WNS-Error-Description"))
                        {
                            errorDescription = response.Headers.GetValues("X-WNS-Error-Description").FirstOrDefault() ?? "Unknown error";
                        }
                        if (response.Headers.Contains("X-WNS-Debug-Trace"))
                        {
                            errorDetails = response.Headers.GetValues("X-WNS-Debug-Trace").FirstOrDefault() ?? "No trace available";
                        }
                    }
                    catch { }
                    
                    StatusUpdated?.Invoke($"❌ Push send failed: {response.StatusCode}");
                    StatusUpdated?.Invoke($"❌ Error: {errorDescription}");
                    StatusUpdated?.Invoke($"❌ Details: {errorDetails}");
                    Debug.WriteLine($"Push notification send failed: {response.StatusCode} - {responseText}");
                    Debug.WriteLine($"WNS Error Description: {errorDescription}");
                    Debug.WriteLine($"WNS Debug Trace: {errorDetails}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Push send exception: {ex.Message}");
                Debug.WriteLine($"SendRawPushNotificationAsync exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Send a toast (app notification) push notification
        /// </summary>
        public static async Task<bool> SendToastPushNotificationAsync(string channelUri, string accessToken, string toastXml)
        {
            try
            {
                StatusUpdated?.Invoke("📤 Sending toast notification...");
                
                using var client = new System.Net.Http.HttpClient();
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, channelUri);
                
                // Set required headers for toast push notification
                request.Headers.Add("Authorization", $"Bearer {accessToken}");
                request.Headers.Add("X-WNS-Type", "wns/toast");
                
                // Set the toast XML payload
                request.Content = new System.Net.Http.StringContent(toastXml, Encoding.UTF8, "text/xml");
                
                var response = await client.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    StatusUpdated?.Invoke($"✅ Toast sent successfully: {response.StatusCode}");
                    Debug.WriteLine($"Toast notification sent successfully: {response.StatusCode}");
                    return true;
                }
                else
                {
                    StatusUpdated?.Invoke($"❌ Toast send failed: {response.StatusCode}");
                    Debug.WriteLine($"Toast notification send failed: {response.StatusCode} - {responseText}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Toast send exception: {ex.Message}");
                Debug.WriteLine($"SendToastPushNotificationAsync exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Get the current channel information
        /// </summary>
        public static (string? Uri, DateTimeOffset? Expiry) GetCurrentChannel()
        {
            if (_currentChannel != null)
            {
                return (_currentChannel.Uri.ToString(), _currentChannel.ExpirationTime);
            }
            return (null, null);
        }

        /// <summary>
        /// Register device with SimplePushServer (for background activation testing)
        /// </summary>
        public static async Task<bool> RegisterWithServerAsync(string userId = "testuser")
        {
            try
            {
                var (channelUri, expiry) = GetCurrentChannel();
                if (string.IsNullOrEmpty(channelUri))
                {
                    StatusUpdated?.Invoke("❌ No channel URI available. Initialize push notifications first.");
                    return false;
                }

                StatusUpdated?.Invoke($"🔄 Registering with SimplePushServer...");
                StatusUpdated?.Invoke($"� User ID: {userId}");

                using var client = new System.Net.Http.HttpClient();
                var registrationData = new
                {
                    channelUri = channelUri,
                    userId = userId
                };

                var json = System.Text.Json.JsonSerializer.Serialize(registrationData);
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync("http://localhost:5000/register", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    StatusUpdated?.Invoke($"✅ Device registered successfully!");
                    StatusUpdated?.Invoke($"🎯 Ready for background activation - you can now close this app");
                    StatusUpdated?.Invoke($"📤 Send notifications via: POST http://localhost:5000/send");
                    Debug.WriteLine($"Registration response: {responseJson}");
                    return true;
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    StatusUpdated?.Invoke($"❌ Registration failed: {response.StatusCode}");
                    StatusUpdated?.Invoke($"❌ Error: {errorText}");
                    Debug.WriteLine($"Registration failed: {response.StatusCode} - {errorText}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Registration exception: {ex.Message}");
                Debug.WriteLine($"RegisterWithServerAsync exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Test the complete push notification flow
        /// </summary>
        public static async Task<bool> TestPushNotificationFlowAsync()
        {
            try
            {
                StatusUpdated?.Invoke("🧪 Starting push notification test flow...");
                
                // Step 1: Initialize push notifications
                var initialized = await InitializeAsync();
                if (!initialized)
                {
                    StatusUpdated?.Invoke("❌ Test failed: Could not initialize push notifications");
                    return false;
                }

                // Step 2: Get channel info
                var (channelUri, expiry) = GetCurrentChannel();
                if (string.IsNullOrEmpty(channelUri))
                {
                    StatusUpdated?.Invoke("❌ Test failed: No channel URI available");
                    return false;
                }

                // Step 3: Get access token
                var accessToken = await RequestAccessTokenAsync();
                if (string.IsNullOrEmpty(accessToken))
                {
                    StatusUpdated?.Invoke("❌ Test failed: Could not obtain access token");
                    return false;
                }

                // Step 4: Send test raw notification
                var testPayload = $"{{ \"message\": \"Test notification\", \"timestamp\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\" }}";
                var rawSuccess = await SendRawPushNotificationAsync(channelUri, accessToken, testPayload);

                if (rawSuccess)
                {
                    StatusUpdated?.Invoke("✅ Push notification test flow completed successfully!");
                    return true;
                }
                else
                {
                    StatusUpdated?.Invoke("❌ Test failed: Could not send push notification");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Test flow exception: {ex.Message}");
                Debug.WriteLine($"TestPushNotificationFlowAsync exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Validate Azure configuration and provide troubleshooting info
        /// </summary>
        public static void ValidateAzureConfiguration()
        {
            StatusUpdated?.Invoke("🔍 Validating Azure configuration...");
            
            // Check if Azure Object ID is the default placeholder
            if (AzureObjectId == new Guid("fb750b7b-c2b6-4106-b556-b9b1fac4d1f6"))
            {
                StatusUpdated?.Invoke("⚠️ Using placeholder Azure Object ID - this will cause BadRequest errors!");
                StatusUpdated?.Invoke("💡 You need to replace AzureObjectId with your actual Enterprise Application Object ID");
                StatusUpdated?.Invoke("💡 Steps to find the correct Object ID:");
                StatusUpdated?.Invoke("   1. Go to Azure Portal > Azure Active Directory > Enterprise Applications");
                StatusUpdated?.Invoke("   2. Find your app registration");
                StatusUpdated?.Invoke("   3. Copy the Object ID from the Enterprise Application (NOT App Registration)");
            }
            else
            {
                StatusUpdated?.Invoke($"✅ Azure Object ID configured: {AzureObjectId}");
            }
            
            StatusUpdated?.Invoke($"📋 Azure App ID: {AzureAppId}");
            StatusUpdated?.Invoke($"📋 Tenant ID: {TenantId}");
            
            // Check if app is packaged and provide PFN info
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                var pfn = package.Id.FamilyName;
                StatusUpdated?.Invoke($"📦 Package Family Name (PFN): {pfn}");
                StatusUpdated?.Invoke("💡 For packaged apps, ensure PFN is mapped to Azure App ID");
                StatusUpdated?.Invoke("💡 Email Win_App_SDK_Push@microsoft.com for PFN mapping if needed");
            }
            catch
            {
                StatusUpdated?.Invoke("📦 App is unpackaged - no PFN mapping needed");
            }
        }

        /// <summary>
        /// Troubleshoot common push notification issues
        /// </summary>
        public static async Task TroubleshootPushIssues()
        {
            StatusUpdated?.Invoke("🔧 Starting push notification troubleshooting...");
            
            // Validate configuration
            ValidateAzureConfiguration();
            
            // Test access token
            StatusUpdated?.Invoke("🔑 Testing access token request...");
            var token = await RequestAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                StatusUpdated?.Invoke("ℹ️ Token request using placeholder credentials (expected behavior)");
                StatusUpdated?.Invoke("💡 SimplePushServer handles actual WNS authentication");
                return;
            }
            
            // Test channel creation
            StatusUpdated?.Invoke("📡 Testing channel creation...");
            try
            {
                var channelResult = await PushNotificationManager.Default.CreateChannelAsync(AzureObjectId);
                if (channelResult.Status == PushNotificationChannelStatus.CompletedSuccess)
                {
                    StatusUpdated?.Invoke("✅ Channel creation successful");
                    StatusUpdated?.Invoke($"📋 Channel URI: {channelResult.Channel.Uri}");
                }
                else
                {
                    StatusUpdated?.Invoke($"❌ Channel creation failed: {channelResult.Status}");
                    if (channelResult.ExtendedError != null)
                    {
                        StatusUpdated?.Invoke($"❌ Error details: {channelResult.ExtendedError.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Channel creation exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleanup resources when app is closing
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                if (_isRegistered)
                {
                    PushNotificationManager.Default.Unregister();
                    _isRegistered = false;
                }
                
                _currentChannel = null;
                StatusUpdated?.Invoke("🧹 Push notification cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup exception: {ex}");
            }
        }

        /// <summary>
        /// Handle background payload with comprehensive logging and safe toast display
        /// </summary>
        public static async Task HandleBackgroundPayloadAsync(string payload)
        {
            try
            {
                // Defensive limits
                if (string.IsNullOrEmpty(payload)) return;
                if (payload.Length > 200_000) payload = payload.Substring(0, 200_000); // clamp

                // Log to disk for offline debugging
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUI3PushLogs");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "background-activation.txt"),
                    $"{DateTime.Now:O} Payload length={payload.Length}\n{payload}\n\n");

                // Parse JSON payload to extract title and message
                string messageText = payload;
                string titleText = "Background Push";

                try
                {
                    using var jsonDoc = System.Text.Json.JsonDocument.Parse(payload);
                    if (jsonDoc.RootElement.TryGetProperty("message", out var msgEl))
                    {
                        messageText = msgEl.GetString() ?? messageText;
                        File.AppendAllText(Path.Combine(logDir, "background-activation.txt"),
                            $"✅ Extracted message: {messageText}\n");
                    }
                    if (jsonDoc.RootElement.TryGetProperty("title", out var tEl))
                    {
                        titleText = tEl.GetString() ?? titleText;
                        File.AppendAllText(Path.Combine(logDir, "background-activation.txt"),
                            $"✅ Extracted title: {titleText}\n");
                    }
                }
                catch (Exception parseEx)
                {
                    File.AppendAllText(Path.Combine(logDir, "background-activation.txt"),
                        $"⚠️ JSON parse failed: {parseEx.Message} - using raw payload\n");
                    // not JSON — use raw payload string
                }

                // Show toast (clamped lengths)
                titleText = titleText.Length > 200 ? titleText.Substring(0, 200) : titleText;
                messageText = messageText.Length > 1000 ? messageText.Substring(0, 1000) : messageText;

                // show the toast (safe call)
                ShowToastSafely(titleText, messageText);
                await Task.Delay(200); // allow toast to register
            }
            catch (Exception ex)
            {
                // best-effort logging
                try
                {
                    File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUI3PushLogs", "errors.txt"),
                        $"{DateTime.Now:O} Exception in HandleBackgroundPayloadAsync: {ex}\n");
                }
                catch { }
            }
        }

        /// <summary>
        /// Show toast notification safely with minimal native API calls
        /// </summary>
        private static void ShowToastSafely(string title, string message)
        {
            try
            {
                // Toast that launches the app when clicked
                var toastXml = $@"<toast launch='app-defined-string'>
                <visual>
                    <binding template='ToastGeneric'>
                        <text>{System.Security.SecurityElement.Escape(title)}</text>
                        <text>{System.Security.SecurityElement.Escape(message)}</text>
                    </binding>
                </visual>
            </toast>";

                var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();
                xmlDoc.LoadXml(toastXml);

                var toast = new Windows.UI.Notifications.ToastNotification(xmlDoc);
                
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch
            {
                // ignore toast failures in background
            }
        }
    }
}