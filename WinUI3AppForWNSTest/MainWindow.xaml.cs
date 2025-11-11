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
        public MainWindow()
        {
            InitializeComponent();
            
            // Subscribe to push notifications
            PushManager.NotificationReceived += OnPushNotificationReceived;
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
                
                var success = await PushManager.InitializeAsync();
                
                if (success)
                {
                    // Automatically register with SimplePushServer after successful initialization
                    AppendLog("🔄 Auto-registering with SimplePushServer...");
                    var registrationSuccess = await PushManager.RegisterWithServerAsync("testuser");
                    
                    if (registrationSuccess)
                    {
                        AppendLog("✅ READY FOR TESTING!");
                        AppendLog("🎯 You can now:");
                        AppendLog("   • Close this app to test background push");
                        AppendLog("   • Send notifications from SimplePushServer");
                        AppendLog("   • App will show toast when push arrives");
                        
                        StatusTextBlock.Text = "✅ Ready for push notifications";
                        StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                    }
                    else
                    {
                        AppendLog("⚠️ Auto-registration failed");
                        StatusTextBlock.Text = "Initialized but registration failed";
                    }
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
