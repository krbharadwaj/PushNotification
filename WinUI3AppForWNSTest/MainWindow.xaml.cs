using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUI3AppForWNSTest
{
    /// <summary>
    /// Dual push notification window - VAPID + WNS
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            
            // Subscribe to WebPush status updates (VAPID)
            WebPushManager.StatusUpdated += (status) => DispatcherQueue.TryEnqueue(() => AppendLog($"[VAPID] {status}"));
            
            // Subscribe to PushManager status updates (WNS)
            PushManager.StatusUpdated += (status) => DispatcherQueue.TryEnqueue(() => AppendLog($"[WNS] {status}"));
            PushManager.ChannelReceived += (uri, expiry) => DispatcherQueue.TryEnqueue(() => {
                AppendLog($"[WNS] ✅ Channel received: {uri.Substring(0, Math.Min(50, uri.Length))}...");
                AppendLog($"[WNS] ⏰ Expires: {expiry:yyyy-MM-dd HH:mm:ss}");
                RegisterWnsButton.IsEnabled = true;
            });
            PushManager.NotificationReceived += (payload) => DispatcherQueue.TryEnqueue(() => AppendLog($"[WNS] 🔔 PUSH RECEIVED: {payload}"));
        }

        private void GenerateKeysButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateKeysButton.IsEnabled = false;
            
            AppendLog("🚀 Step 1: Generating VAPID keys...");
            
            bool success = WebPushManager.GenerateVapidKeys();
            
            if (success)
            {
                SubscribeButton.IsEnabled = true;
                AppendLog("✅ Keys generated! Now click Subscribe to create channel.");
            }
            
            GenerateKeysButton.IsEnabled = true;
        }

        private async void SubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            SubscribeButton.IsEnabled = false;
            
            AppendLog("🚀 Step 2: Creating subscription and sending to server...");
            
            bool success = await WebPushManager.SubscribeAsync();
            
            if (success)
            {
                AppendLog("🎉 Complete! SimplePushServer can now send push messages.");
            }
            
            SubscribeButton.IsEnabled = true;
        }

        // =========================================================================
        // WNS Push Notification Handlers (Traditional Implementation)
        // =========================================================================

        private async void InitializeWnsButton_Click(object sender, RoutedEventArgs e)
        {
            InitializeWnsButton.IsEnabled = false;
            
            AppendLog("[WNS] 🚀 Initializing traditional WNS push notifications...");
            
            bool success = await PushManager.InitializeAsync();
            
            if (success)
            {
                AppendLog("[WNS] ✅ WNS initialization completed! Channel created.");
                // RegisterWnsButton will be enabled automatically by the ChannelReceived event
            }
            else
            {
                AppendLog("[WNS] ❌ WNS initialization failed.");
            }
            
            InitializeWnsButton.IsEnabled = true;
        }

        private async void RegisterWnsButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterWnsButton.IsEnabled = false;
            
            AppendLog("[WNS] 📤 Registering with SimplePushServer (WNS endpoint)...");
            
            bool success = await PushManager.RegisterWithServerAsync("winui3-wns-device", "testuser");
            
            if (success)
            {
                AppendLog("[WNS] 🎉 Complete! SimplePushServer can now send WNS push messages.");
                AppendLog("[WNS] 💡 Use POST http://localhost:5000/send to test");
            }
            else
            {
                AppendLog("[WNS] ❌ Registration with server failed.");
            }
            
            RegisterWnsButton.IsEnabled = true;
        }
        
        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var currentText = LogTextBlock.Text;
            
            if (currentText == "Choose your push notification implementation above...")
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