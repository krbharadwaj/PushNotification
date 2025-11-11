using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.PushNotifications;

using IOPath = System.IO.Path;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3AppForWNSTest
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // === DIAGNOSTIC LOGGING - Capture how Windows launches the app ===
            try
            {
                // Try multiple log locations for unpackaged apps
                string logDir = null;
                try
                {
                    logDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUI3PushLogs");
                }
                catch
                {
                    logDir = IOPath.Combine(Path.GetTempPath(), "WinUI3PushLogs");
                }
                
                Directory.CreateDirectory(logDir);
                string logPath = IOPath.Combine(logDir, $"launch-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("=== Launch diagnostic ===");
                sb.AppendLine($"Time: {DateTime.Now:O}");
                sb.AppendLine($"CommandLine: {Environment.CommandLine}");
                sb.AppendLine($"Args (from LaunchActivatedEventArgs may differ):");
                sb.AppendLine($"LogDirectory: {logDir}");

                // record environment variables (useful)
                sb.AppendLine("Environment Variables:");
                foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
                {
                    sb.AppendLine($"  {de.Key} = {de.Value}");
                }

                // record process bitness and working dir
                sb.AppendLine($"Process64Bit: {Environment.Is64BitProcess}");
                sb.AppendLine($"WorkingDirectory: {Environment.CurrentDirectory}");

                File.WriteAllText(logPath, sb.ToString());
                System.Diagnostics.Debug.WriteLine($"Launch log written to: {logPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write launch log: {ex}");
            }
            
            InitializeComponent();
            
            // Add diagnostic exception handlers
            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[FirstChance] {e.Exception.GetType().Name}: {e.Exception.Message}");
            };
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // === DIAGNOSTIC: Detect background push activation ===
            try
            {
                // Get command line - args.Arguments is often empty, use Environment.CommandLine
                string cmdLine = Environment.CommandLine ?? string.Empty;
                string argsFromEvent = args.Arguments ?? string.Empty;
                
                string logDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUI3PushLogs");
                Directory.CreateDirectory(logDir);
                
                // ALWAYS log every launch for debugging
                File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                    $"\n\n============================================================\n{DateTime.Now:O} APP LAUNCHED\n============================================================\n");
                File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                    $"Environment.CommandLine: {cmdLine}\n");
                File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                    $"args.Arguments: {argsFromEvent}\n");

                // Check if launched with push server argument (from either source)
                bool isPushActivation = cmdLine.Contains("----WindowsAppRuntimePushServer:", StringComparison.OrdinalIgnoreCase) ||
                                       argsFromEvent.Contains("----WindowsAppRuntimePushServer:", StringComparison.OrdinalIgnoreCase);
                
                if (isPushActivation)
                {
                    // We're being launched as a push activation helper
                    File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                        $"{DateTime.Now:O} PUSH ACTIVATION DETECTED\n");

                    // TRY: check if payload is passed via environment variable
                    var envPayload = Environment.GetEnvironmentVariable("WindowsAppRuntimePushServerPayload");
                    if (!string.IsNullOrEmpty(envPayload))
                    {
                        File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                            $"✅ Found env payload: {envPayload}\n");
                        await PushManager.HandleBackgroundPayloadAsync(envPayload);
                    }
                    else
                    {
                        File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                            "❌ ERROR: No payload env var found - cannot show notification\n");
                        
                        // Log all environment variables for debugging
                        var allVars = Environment.GetEnvironmentVariables();
                        File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                            "\nAll environment variables:\n");
                        foreach (System.Collections.DictionaryEntry de in allVars)
                        {
                            File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                                $"  {de.Key} = {de.Value}\n");
                        }
                    }

                    // Important: shut down the background helper promptly
                    await System.Threading.Tasks.Task.Delay(500); // allow toast to register
                    Application.Current.Exit();
                    return;
                }
                
                // Also check using AppLifecycle API
                var activationArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                
                File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                    $"{DateTime.Now:O} Activation Kind: {activationArgs.Kind}\n");
                
                // Handle background push notification via AppLifecycle
                if (activationArgs.Kind == ExtendedActivationKind.Push)
                {
                    File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                        "Push activation detected via AppLifecycle\n");
                    
                    if (activationArgs.Data is PushNotificationReceivedEventArgs pushArgs)
                    {
                        var payload = pushArgs.Payload;
                        if (payload != null && payload.Length > 0)
                        {
                            var payloadString = System.Text.Encoding.UTF8.GetString(payload);
                            File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                                $"✅ USING ACTUAL PUSH PAYLOAD from AppLifecycle: {payloadString}\n");
                            
                            // Use the actual payload from the push notification
                            await PushManager.HandleBackgroundPayloadAsync(payloadString);
                        }
                        else
                        {
                            File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                                "❌ ERROR: Push activation but no payload available\n");
                        }
                    }
                    else
                    {
                        File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                            "❌ ERROR: Push activation but PushNotificationReceivedEventArgs is null\n");
                    }
                    
                    // Exit after processing
                    await System.Threading.Tasks.Task.Delay(500);
                    Application.Current.Exit();
                    return;
                }
                
                // Check for toast activation (when user clicks on a notification)
                if (activationArgs.Kind == ExtendedActivationKind.ToastNotification)
                {
                    File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                        $"{DateTime.Now:O} Toast activation detected - launching main window\n");
                    // Continue to launch the main window below
                }
                
                // ✅ CRITICAL FIX: Register push notifications EARLY on normal launch only
                // This ensures COM activation CLSID mapping is cached by Windows
                // Skip registration on background/toast activation to prevent conflicts
                if (activationArgs.Kind == ExtendedActivationKind.Launch || 
                    activationArgs.Kind == ExtendedActivationKind.ToastNotification)
                {
                    File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                        $"{DateTime.Now:O} Registering push notifications on startup...\n");
                    
                    try
                    {
                        // Initialize push notifications synchronously on startup
                        var initSuccess = await PushManager.InitializeAsync();
                        File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                            $"{DateTime.Now:O} Push registration result: {initSuccess}\n");
                    }
                    catch (Exception regEx)
                    {
                        File.AppendAllText(IOPath.Combine(logDir, "launch-args.txt"), 
                            $"{DateTime.Now:O} Push registration error: {regEx.Message}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                // best-effort logging
                try
                {
                    string logDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUI3PushLogs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(IOPath.Combine(logDir, "errors.txt"), 
                        $"{DateTime.Now:O} Exception in OnLaunched: {ex}\n");
                }
                catch { }
            }

            // Normal UI startup
            _window = new MainWindow();
            _window.Activate();
        }

    }
}
