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
        // Azure AD Application Registration Details
        // IMPORTANT: Replace with your actual Azure AD app registration Object ID
        // This should be the Object ID from the Enterprise Application (Service Principal), NOT the App Registration Object ID
        // See SECRETS.md (local file) for actual working credentials
        private static readonly Guid AzureObjectId = new Guid("YOUR_AZURE_OBJECT_ID_HERE"); // Replace with your Azure Object ID
        private static readonly Guid AzureAppId = new Guid("YOUR_AZURE_APP_ID_HERE");
        private const string TenantId = "YOUR_TENANT_ID_HERE";
        private const string ClientSecret = "YOUR_CLIENT_SECRET_HERE";

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
                
                Debug.WriteLine($"Push notification received (foreground): {payloadString}");
                StatusUpdated?.Invoke($"📩 Push received: {payloadString}");
                
                // Notify subscribers about the received notification
                NotificationReceived?.Invoke(payloadString);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnPushReceived exception: {ex}");
                StatusUpdated?.Invoke($"❌ Error processing push notification: {ex.Message}");
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
                        StatusUpdated?.Invoke("📩 App activated by push notification");
                        
                        // Handle background push activation
                        if (args.Data is PushNotificationReceivedEventArgs pushArgs)
                        {
                            // Get deferral to ensure proper background processing
                            var deferral = pushArgs.GetDeferral();
                            
                            try
                            {
                            // Process the background push notification
                            var payload = pushArgs.Payload;
                            var payloadString = Encoding.UTF8.GetString(payload);                                Debug.WriteLine($"Background push notification: {payloadString}");
                                StatusUpdated?.Invoke($"📩 Background push: {payloadString}");
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
                StatusUpdated?.Invoke("🔑 Requesting access token...");
                
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
                    StatusUpdated?.Invoke($"❌ Token request failed: {response.StatusCode}");
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
                
                StatusUpdated?.Invoke("❌ Access token not found in response");
                return null;
            }
            catch (Exception ex)
            {
                StatusUpdated?.Invoke($"❌ Token request exception: {ex.Message}");
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
                StatusUpdated?.Invoke("❌ Access token request failed - check Azure credentials");
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
    }
}