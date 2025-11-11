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
    /// Main window with WebView2 and push notification support
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private List<string> _steps = new List<string>();
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize WebView2
            InitializeWebView();
            
            // Subscribe to push notifications
            PushManager.NotificationReceived += OnPushNotificationReceived;
        }
        
        private async void InitializeWebView()
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 initialization error: {ex.Message}");
            }
        }
        
        private void OnPushNotificationReceived(string payload)
        {
            // Log in WebView console
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (WebView.CoreWebView2 != null)
                    {
                        var escapedPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                        await WebView.CoreWebView2.ExecuteScriptAsync($"console.log('Push received: ' + {escapedPayload})");
                    }
                }
                catch { }
            });
        }
        
        private async void InitializePushButton_Click(object sender, RoutedEventArgs e)
        {
            InitializePushButton.IsEnabled = false;
            _steps.Clear();
            StatusBorder.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "Initializing Push Notifications...";
            StepsTextBlock.Text = "";
            
            try
            {
                // Subscribe to status updates
                PushManager.StatusUpdated += OnStatusUpdated;
                
                AddStep("🔧 Requesting push notification channel...");
                var success = await PushManager.InitializeAsync();
                
                if (success)
                {
                    AddStep("✅ Push channel created successfully");
                    
                    AddStep("📡 Registering with SimplePushServer...");
                    await PushManager.RegisterWithServerAsync("testuser");
                    AddStep("✅ Registered with server");
                    
                    StatusTextBlock.Text = "✅ Initialization Complete!";
                    
                    // Show success in WebView console
                    if (WebView.CoreWebView2 != null)
                    {
                        await WebView.CoreWebView2.ExecuteScriptAsync("console.log('✅ Push notifications initialized and registered!')");
                    }
                }
                else
                {
                    AddStep("❌ Failed to create push channel");
                    StatusTextBlock.Text = "❌ Initialization Failed";
                }
            }
            catch (Exception ex)
            {
                AddStep($"❌ Error: {ex.Message}");
                StatusTextBlock.Text = "❌ Initialization Failed";
                
                if (WebView.CoreWebView2 != null)
                {
                    var escapedError = System.Text.Json.JsonSerializer.Serialize(ex.Message);
                    await WebView.CoreWebView2.ExecuteScriptAsync($"console.error('Push initialization failed: ' + {escapedError})");
                }
            }
            finally
            {
                InitializePushButton.IsEnabled = true;
            }
        }
        
        private void AddStep(string step)
        {
            _steps.Add(step);
            DispatcherQueue.TryEnqueue(() =>
            {
                StepsTextBlock.Text = string.Join("\n", _steps);
            });
        }
        
        private void OnStatusUpdated(string status)
        {
            AddStep($"ℹ️ {status}");
            
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (WebView.CoreWebView2 != null)
                    {
                        var escapedStatus = System.Text.Json.JsonSerializer.Serialize(status);
                        await WebView.CoreWebView2.ExecuteScriptAsync($"console.log({escapedStatus})");
                    }
                }
                catch { }
            });
        }
    }
}
