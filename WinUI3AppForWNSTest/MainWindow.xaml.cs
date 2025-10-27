using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3AppForWNSTest
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private string? _channelUri;
        private string? _accessToken;
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Subscribe to push notifications
            PushManager.NotificationReceived += OnPushNotificationReceived;
            
            // Set default test message
            TestMessageTextBox.Text = "Hello from WinUI3 Push Test!";
        }
        
        private void OnPushNotificationReceived(string payload)
        {
            // Update UI on the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendLog($"📩 Push notification received: {payload}");
            });
        }
        
        private void OnStatusUpdated(string status)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusTextBlock.Text = status;
                
                if (status.Contains("✅"))
                {
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                }
                else if (status.Contains("❌"))
                {
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }
                else if (status.Contains("🔄"))
                {
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                }
                
                AppendLog(status);
            });
        }
        
        private void OnChannelReceived(string uri, DateTimeOffset expiry)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _channelUri = uri;
                AppendLog($"📋 Channel URI received: {uri}");
                AppendLog($"⏰ Expires: {expiry:yyyy-MM-dd HH:mm:ss}");
                
                // Enable registration button when channel is available
                RegisterWithServerButton.IsEnabled = true;
            });
        }
        
        private async void InitializePushButton_Click(object sender, RoutedEventArgs e)
        {
            InitializePushButton.IsEnabled = false;
            StatusTextBlock.Text = "Initializing...";
            StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
            
            AppendLog("🔄 Starting push notification initialization...");
            
            try
            {
                // Subscribe to status updates
                PushManager.StatusUpdated += OnStatusUpdated;
                PushManager.ChannelReceived += OnChannelReceived;
                
                var success = await PushManager.InitializeAsync();
                
                if (success)
                {
                    // Automatically register with SimplePushServer after successful initialization
                    AppendLog("🔄 Auto-registering device with SimplePushServer...");
                    var registrationSuccess = await PushManager.RegisterWithServerAsync("winui3-device", "testuser");
                    
                    if (registrationSuccess)
                    {
                        AppendLog("✅ DEVICE AUTO-REGISTERED SUCCESSFULLY!");
                        AppendLog("🎯 READY FOR BACKGROUND TESTING:");
                        AppendLog("   ✓ Device registered with server");
                        AppendLog("   ✓ Close this app and send notifications from SimplePushServer");
                        AppendLog("   ✓ App will activate automatically on push notifications!");
                        
                        // Update status
                        StatusTextBlock.Text = "Auto-registered - Ready for background activation";
                        StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                        
                        // Disable manual registration button since it's already done
                        RegisterWithServerButton.IsEnabled = false;
                        RegisterWithServerButton.Content = "✅ Already Registered";
                    }
                    else
                    {
                        AppendLog("⚠️ Auto-registration failed, you can try manual registration");
                        RegisterWithServerButton.IsEnabled = true;
                    }
                    
                    // Try to get access token for testing
                    AppendLog("🔄 Requesting access token for testing...");
                    _accessToken = await PushManager.RequestAccessTokenAsync();
                }
                
                if (!string.IsNullOrEmpty(_accessToken))
                {
                    AppendLog("✅ Access token obtained successfully");
                    SendTestPushButton.IsEnabled = true;
                }
                else
                {
                    AppendLog("⚠️ Could not obtain access token (normal for client-side apps)");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Initialization failed: {ex.Message}");
                StatusTextBlock.Text = "Initialization failed";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            finally
            {
                InitializePushButton.IsEnabled = true;
            }
        }

        private async void RegisterWithServerButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterWithServerButton.IsEnabled = false;
            
            try
            {
                AppendLog("🔄 Registering device with SimplePushServer for background activation...");
                
                var success = await PushManager.RegisterWithServerAsync("winui3-device", "testuser");
                
                if (success)
                {
                    AppendLog("🎯 READY FOR BACKGROUND TESTING:");
                    AppendLog("   1. Close this WinUI3 app completely");
                    AppendLog("   2. Use SimplePushServer to send notifications");
                    AppendLog("   3. WinUI3 app will activate in background!");
                    
                    // Update status
                    StatusTextBlock.Text = "Registered - Ready for background activation";
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Registration failed: {ex.Message}");
                StatusTextBlock.Text = "Registration failed";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            finally
            {
                RegisterWithServerButton.IsEnabled = true;
            }
        }
        
        private async void SendTestPushButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_channelUri) || string.IsNullOrEmpty(_accessToken))
            {
                AppendLog("❌ Cannot send push: Channel URI or access token not available");
                return;
            }
            
            SendTestPushButton.IsEnabled = false;
            
            try
            {
                var testMessage = TestMessageTextBox.Text;
                if (string.IsNullOrEmpty(testMessage))
                {
                    testMessage = "Test notification from WinUI3 app";
                }
                
                AppendLog($"📤 Sending test push notification: {testMessage}");
                
                var success = await PushManager.SendRawPushNotificationAsync(_channelUri, _accessToken, testMessage);
                
                if (success)
                {
                    AppendLog("✅ Test push notification sent successfully");
                }
                else
                {
                    AppendLog("❌ Failed to send test push notification");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Failed to send push notification: {ex.Message}");
            }
            finally
            {
                SendTestPushButton.IsEnabled = true;
            }
        }
        
        private void UpdateStatus(string message, string uri, string expiration)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusTextBlock.Text = message;
                
                if (message.Contains("✅") && message.Contains("Channel created"))
                {
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                    _channelUri = uri;
                    AppendLog($"📋 Channel URI: {uri}");
                    AppendLog($"⏰ Expires: {expiration}");
                }
                else if (message.Contains("❌"))
                {
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }
                else if (message.Contains("🔄"))
                {
                    StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                }
                
                AppendLog(message);
            });
        }
        
        private async void TroubleshootButton_Click(object sender, RoutedEventArgs e)
        {
            TroubleshootButton.IsEnabled = false;
            
            try
            {
                AppendLog("🔧 Starting troubleshooting...");
                await PushManager.TroubleshootPushIssues();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Troubleshooting failed: {ex.Message}");
            }
            finally
            {
                TroubleshootButton.IsEnabled = true;
            }
        }
        
        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var currentText = LogTextBlock.Text;
            
            if (currentText == "Log will appear here...")
            {
                LogTextBlock.Text = $"[{timestamp}] {message}";
            }
            else
            {
                LogTextBlock.Text = $"{currentText}\n[{timestamp}] {message}";
            }
        }
    }
}
