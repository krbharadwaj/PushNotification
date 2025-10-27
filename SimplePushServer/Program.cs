using System.Text;
using System.Text.Json;
using SimplePushServer.WebPush;

// =============================================================================
// Request/Response Models
// =============================================================================

// WNS Models (Traditional Windows Push Notifications)
public record WnsRegisterRequest(string DeviceId, string ChannelUri, string UserId);
public record WnsSendRequest(string DeviceId, string Message, string? Title = null);

// VAPID Models (New Web Push with VAPID)
public record VapidSubscribeRequest(string DeviceId, string ChannelUri, string PrivateKey);
public record VapidSendRequest(string DeviceId, string Message, string? Title = null);

// Storage Models
public record WnsDevice(string DeviceId, string ChannelUri, string UserId, DateTime RegisteredAt);
public record VapidDevice(string DeviceId, string ChannelUri, string PrivateKey, DateTime RegisteredAt);

class Program
{
    // Storage for both implementations
    private static readonly Dictionary<string, WnsDevice> _wnsDevices = new();
    private static readonly Dictionary<string, VapidDevice> _vapidDevices = new();

    /// <summary>
    /// Load secrets from SECRETS.config file (same as WinUI3 app)
    /// </summary>
    private static Dictionary<string, string> LoadSecrets()
    {
        var secrets = new Dictionary<string, string>();
        
        // Try multiple possible locations for SECRETS.config
        var possiblePaths = new[]
        {
            // Current directory (SimplePushServer)
            Path.Combine(Directory.GetCurrentDirectory(), "SECRETS.config"),
            // Parent directory (solution root)
            Path.Combine(Directory.GetCurrentDirectory(), "..", "SECRETS.config"),
            // WinUI3AppForWNSTest directory (sibling)
            Path.Combine(Directory.GetCurrentDirectory(), "..", "WinUI3AppForWNSTest", "SECRETS.config"),
            // Solution root from typical build output
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "SECRETS.config"),
            // Relative to project directory
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SECRETS.config")
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
                Console.WriteLine($"⚠️ Error checking path {path}: {ex.Message}");
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
                Console.WriteLine($"✅ Loaded {secrets.Count} secrets from: {foundPath}");
                Console.WriteLine($"🔑 TenantId: {secrets.GetValueOrDefault("TenantId", "not found")}");
                Console.WriteLine($"🔑 ClientId: {secrets.GetValueOrDefault("ClientId", "not found")}");
                Console.WriteLine($"🔑 ClientSecret: {(string.IsNullOrEmpty(secrets.GetValueOrDefault("ClientSecret")) ? "not found" : "***found***")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading SECRETS.config: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"⚠️ SECRETS.config not found in any of the expected locations:");
            foreach (var path in possiblePaths)
            {
                try
                {
                    Console.WriteLine($"  - {Path.GetFullPath(path)}");
                }
                catch
                {
                    Console.WriteLine($"  - {path} (invalid path)");
                }
            }
            Console.WriteLine("💡 Please ensure SECRETS.config exists with TenantId, ClientId, and ClientSecret");
        }
        return secrets;
    }

    // WNS Configuration (from SECRETS.config)
    private static readonly Lazy<Dictionary<string, string>> _secrets = new(() => LoadSecrets());
    private static readonly string TenantId = _secrets.Value.GetValueOrDefault("TenantId") ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? "your-tenant-id";
    private static readonly string ClientId = _secrets.Value.GetValueOrDefault("ClientId") ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? "your-client-id";
    private static readonly string ClientSecret = _secrets.Value.GetValueOrDefault("ClientSecret") ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? "your-client-secret";

    static void Main(string[] args)
    {
        Console.WriteLine("🚀 Simple Push Server - WNS + VAPID Support");
        Console.WriteLine("📡 Supporting both traditional WNS and modern VAPID push notifications");
        Console.WriteLine();
        
        // Load and display configuration
        Console.WriteLine("⚙️ Loading configuration from SECRETS.config...");
        _ = _secrets.Value; // Force loading of secrets
        Console.WriteLine();

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHttpClient();
        
        // Add Swagger services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { 
                Title = "Push Notification Server API", 
                Version = "v1",
                Description = "Dual Push Notification Server supporting both VAPID and WNS implementations"
            });
        });

        var app = builder.Build();
        
        // Configure Swagger middleware
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Push Notification Server API v1");
                c.RoutePrefix = "swagger";
            });
        }

        // =============================================================================
        // VAPID Implementation (New - for WinUI3 WebPushManager)
        // =============================================================================

        app.MapPost("/subscribe", (VapidSubscribeRequest request) =>
        {
            try
            {
                var device = new VapidDevice(request.DeviceId, request.ChannelUri, request.PrivateKey, DateTime.UtcNow);
                _vapidDevices[request.DeviceId] = device;
                
                Console.WriteLine($"✅ VAPID Device registered: {request.DeviceId}");
                Console.WriteLine($"📡 Channel URI: {request.ChannelUri}");
                Console.WriteLine($"🔐 Private key stored for VAPID authentication");
                
                return Results.Ok(new { 
                    success = true, 
                    message = "VAPID device registered successfully",
                    deviceId = request.DeviceId,
                    type = "vapid"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ VAPID Registration failed: {ex.Message}");
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        })
        .WithName("SubscribeVapid")
        .WithSummary("Register VAPID Device")
        .WithDescription("Register a device for VAPID push notifications with channel URI and private key");

        // Simplified VAPID send endpoint - uses first available device (NO JSON PARSING)
        app.MapPost("/vapid/send-test", async () =>
        {
            try
            {
                // Completely hardcoded test message - no parsing at all
                var message = "🔥 Hardcoded VAPID test notification from SimplePushServer";
                var title = "✅ VAPID Success Test";
                
                Console.WriteLine("🧪 Sending hardcoded VAPID/Web Push test message (no JSON parsing)");

                // Get first VAPID device
                var firstDevice = _vapidDevices.Values.FirstOrDefault();
                if (firstDevice == null)
                {
                    return Results.NotFound(new { success = false, message = "No VAPID devices registered. Register a device first using /subscribe." });
                }

                Console.WriteLine($"📤 Sending Web Push to first device ({firstDevice.DeviceId}): {message}");

                using var httpClient = new HttpClient();
                
                Console.WriteLine($"🔗 Channel URI: {firstDevice.ChannelUri.Substring(0, Math.Min(60, firstDevice.ChannelUri.Length))}...");
                
                // Check if this is a Web Push endpoint or traditional WNS
                var isWebPushEndpoint = firstDevice.ChannelUri.Contains("notify.windows.com/w/");
                
                Console.WriteLine($"🔍 Endpoint Type: {(isWebPushEndpoint ? "Web Push (requires VAPID)" : "Traditional WNS Raw")}");
                
                if (isWebPushEndpoint)
                {
                    Console.WriteLine("✅ Detected Web Push endpoint - using proper VAPID authentication");
                    
                    // Create proper Web Push VAPID request (minimal like the example)
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, firstDevice.ChannelUri);
                    
                    // Extract VAPID keys from the registered device
                    Console.WriteLine($"🔐 Using VAPID keys from registered device");
                    Console.WriteLine($"📱 Device Private Key: {firstDevice.PrivateKey.Substring(0, Math.Min(20, firstDevice.PrivateKey.Length))}...");
                    
                    // Declare variables outside try block for proper scope
                    string jwtToken;
                    string vapidPublicKey;
                    
                    try
                    {
                        Console.WriteLine("✅ Generating proper VAPID JWT using device's actual private key");
                        
                        // Extract the actual public key from the device's private key
                        var (publicKeyBase64Url, ecdsaKey) = VapidHelper.ExtractPublicKeyFromPrivateKey(firstDevice.PrivateKey);
                        
                        Console.WriteLine($"� Derived Public Key: {publicKeyBase64Url.Substring(0, Math.Min(20, publicKeyBase64Url.Length))}...");
                        
                        // Get audience from channel URI
                        var audience = VapidHelper.GetAudienceFromChannelUri(firstDevice.ChannelUri);
                        Console.WriteLine($"🎯 JWT Audience: {audience}");
                        
                        // Generate proper VAPID JWT using device's actual keys
                        jwtToken = VapidHelper.GenerateVapidJwt(audience, "mailto:test@example.com", ecdsaKey);
                        vapidPublicKey = publicKeyBase64Url;
                        
                        Console.WriteLine($"� Generated JWT: {jwtToken.Substring(0, Math.Min(30, jwtToken.Length))}...");
                        
                        // Dispose the ECDsa key after use
                        ecdsaKey.Dispose();
                        
                        Console.WriteLine("✅ VAPID authentication prepared with device's actual keys");
                    }
                    catch (Exception keyEx)
                    {
                        Console.WriteLine($"❌ Failed to extract VAPID keys: {keyEx.Message}");
                        Console.WriteLine("🔄 Falling back to Web Push without VAPID (this will likely fail)");
                        
                        // Fallback values (will likely fail but allows testing)
                        jwtToken = "invalid";
                        vapidPublicKey = "invalid";
                    }
                    
                    Console.WriteLine("\n📋 === WEB PUSH REQUEST STRUCTURE ===");
                    Console.WriteLine($"Method: {httpRequest.Method}");
                    Console.WriteLine($"URI: {httpRequest.RequestUri}");
                    
                    // Set proper Web Push headers (minimal, exactly like the working example)
                    httpRequest.Headers.Add("TTL", "60");
                    httpRequest.Headers.Add("Authorization", $"vapid t={jwtToken}, k={vapidPublicKey}");
                    
                    // Empty content for simple notification (like the working example)
                    httpRequest.Content = new ByteArrayContent(new byte[0]);
                    
                    Console.WriteLine("📝 Headers:");
                    Console.WriteLine($"  TTL: 60");
                    Console.WriteLine($"  Authorization: vapid t={jwtToken.Substring(0, 20)}..., k={vapidPublicKey.Substring(0, 20)}...");
                    Console.WriteLine($"  Content-Length: 0");
                    Console.WriteLine("📄 Payload: (empty - simple notification)");
                    
                    Console.WriteLine("\n📤 === SENDING WEB PUSH REQUEST ===");
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    
                    try
                    {
                        var response = await httpClient.SendAsync(httpRequest);
                        stopwatch.Stop();
                        
                        Console.WriteLine("\n📥 === RESPONSE RECEIVED ===");
                        Console.WriteLine($"⏱️ Request Duration: {stopwatch.ElapsedMilliseconds}ms");
                        Console.WriteLine($"📊 Status Code: {(int)response.StatusCode} {response.StatusCode}");
                        Console.WriteLine($"🏷️ Reason Phrase: {response.ReasonPhrase}");
                        
                        Console.WriteLine("📝 Response Headers:");
                        foreach (var header in response.Headers)
                        {
                            Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
                        }
                        if (response.Content.Headers != null)
                        {
                            foreach (var header in response.Content.Headers)
                            {
                                Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
                            }
                        }
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"\n✅ SUCCESS: Web Push notification sent to {firstDevice.DeviceId}");
                            Console.WriteLine($"📄 Response: {(string.IsNullOrEmpty(responseContent) ? "(empty - normal for Web Push)" : responseContent)}");
                            
                            return Results.Ok(new { 
                                success = true, 
                                message = "Web Push notification sent successfully",
                                deviceId = firstDevice.DeviceId,
                                type = "web_push",
                                statusCode = response.StatusCode,
                                response = responseContent
                            });
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"\n❌ ERROR: Web Push failed - {response.StatusCode}");
                            Console.WriteLine($"📄 Error: {(string.IsNullOrEmpty(error) ? "(empty)" : error)}");
                            
                            return Results.BadRequest(new { 
                                success = false, 
                                message = $"Web Push failed: {response.StatusCode} - {error}",
                                deviceId = firstDevice.DeviceId,
                                type = "web_push",
                                statusCode = response.StatusCode
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        Console.WriteLine($"\n❌ WEB PUSH EXCEPTION after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
                        return Results.BadRequest(new { 
                            success = false, 
                            message = $"Web Push error: {ex.Message}",
                            deviceId = firstDevice.DeviceId,
                            type = "web_push",
                            error = ex.GetType().Name
                        });
                    }
                }
                
                // Traditional WNS Raw notification path (if not Web Push endpoint)
                var payload = JsonSerializer.Serialize(new
                {
                    title = title ?? "WNS Raw Test Notification",
                    message = message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    type = "wns_raw"
                });
                
                var wnsRequest = new HttpRequestMessage(HttpMethod.Post, firstDevice.ChannelUri);
                
                Console.WriteLine("🔑 Attempting to get WNS access token...");
                var token = await GetWnsTokenAsync(httpClient);
                
                if (token != null)
                {
                    Console.WriteLine("✅ WNS token obtained successfully");
                    wnsRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    wnsRequest.Headers.Add("X-WNS-Type", "wns/raw");
                    wnsRequest.Headers.Add("X-WNS-RequestForStatus", "true");
                    wnsRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    
                    var wnsResponse = await httpClient.SendAsync(wnsRequest);
                    
                    if (wnsResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"✅ SUCCESS: WNS push sent to {firstDevice.DeviceId}");
                        return Results.Ok(new { 
                            success = true, 
                            message = "WNS push notification sent to first device",
                            deviceId = firstDevice.DeviceId,
                            type = "wns_raw"
                        });
                    }
                    else
                    {
                        var error = await wnsResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"❌ ERROR: WNS push failed: {wnsResponse.StatusCode} - {error}");
                        return Results.BadRequest(new { 
                            success = false, 
                            message = $"WNS failed: {wnsResponse.StatusCode} - {error}",
                            deviceId = firstDevice.DeviceId,
                            type = "wns_raw"
                        });
                    }
                }
                else
                {
                    Console.WriteLine("❌ No WNS token available");
                    return Results.BadRequest(new { 
                        success = false, 
                        message = "WNS authentication failed - no access token available",
                        deviceId = firstDevice.DeviceId,
                        type = "wns_raw"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ VAPID send error: {ex.Message}");
                return Results.BadRequest(new { success = false, message = ex.Message, type = "vapid" });
            }
        })
        .WithName("SendVapidTest")
        .WithSummary("Send Web Push Test (Hardcoded)")
        .WithDescription("Send a hardcoded test notification using proper Web Push VAPID protocol - no request body needed");

        // =============================================================================
        // Common Endpoints
        // =============================================================================

        app.MapGet("/", () => new { 
            service = "Dual Push Notification Server", 
            status = "running",
            timestamp = DateTime.UtcNow,
            wnsDevices = _wnsDevices.Count,
            vapidDevices = _vapidDevices.Count,
            endpoints = new
            {
                vapid = new { register = "POST /subscribe", send = "POST /vapid/send-test" },
                webpush = new { test = "POST /vapid/send-test (detects Web Push endpoints automatically)" }
            }
        })
        .WithName("GetServerInfo")
        .WithSummary("Get Server Status")
        .WithDescription("Get server status, device counts, and available endpoints");

        app.MapGet("/devices", () => Results.Ok(new
        {
            vapid = _vapidDevices.Values.Select(d => new { d.DeviceId, d.RegisteredAt, type = "vapid" }),
            totals = new { vapid = _vapidDevices.Count }
        }));

        // =============================================================================
        // Helper Methods
        // =============================================================================

        Console.WriteLine("✅ Web Push VAPID Server started!");
        Console.WriteLine("📡 Listening on: http://localhost:5000");
        Console.WriteLine("🔗 Endpoints:");
        Console.WriteLine("   Web Push: POST /subscribe, POST /vapid/send-test");
        Console.WriteLine("   Status: GET /, GET /devices");
        Console.WriteLine("💡 Swagger UI: http://localhost:5000/swagger");

        // Configure to run on port 5000 (can be overridden by --urls parameter)
        if (args.Length == 0)
        {
            app.Run("http://localhost:5000");
        }
        else
        {
            app.Run();
        }
    }

    // =============================================================================
    // WNS Helper Methods
    // =============================================================================

    static async Task<string?> GetWnsTokenAsync(HttpClient httpClient)
    {
        try
        {
            var tokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
            
            Console.WriteLine("\n🔐 === WNS TOKEN REQUEST ===");
            Console.WriteLine($"Endpoint: {tokenEndpoint}");
            Console.WriteLine($"Grant Type: client_credentials");
            Console.WriteLine($"Client ID: {ClientId}");
            Console.WriteLine($"Client Secret: {ClientSecret.Substring(0, Math.Min(8, ClientSecret.Length))}...");
            Console.WriteLine($"Scope: https://wns.windows.com/.default");
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("client_secret", ClientSecret),
                new KeyValuePair<string, string>("scope", "https://wns.windows.com/.default")
            });

            var tokenStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await httpClient.PostAsync(tokenEndpoint, content);
            tokenStopwatch.Stop();
            
            Console.WriteLine($"\n🔐 === WNS TOKEN RESPONSE ===");
            Console.WriteLine($"⏱️ Duration: {tokenStopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"📊 Status: {(int)response.StatusCode} {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"📄 Response Length: {responseJson.Length} chars");
                
                var tokenDoc = JsonDocument.Parse(responseJson);
                var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
                
                Console.WriteLine($"✅ Access Token: {accessToken?.Substring(0, Math.Min(20, accessToken?.Length ?? 0))}...");
                
                if (tokenDoc.RootElement.TryGetProperty("expires_in", out var expiresIn))
                {
                    Console.WriteLine($"⏰ Expires In: {expiresIn.GetInt32()} seconds");
                }
                
                return accessToken;
            }
            else
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Token request failed: {response.StatusCode}");
                Console.WriteLine($"📄 Error Response: {errorResponse}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n💥 WNS Token Exception: {ex.Message}");
            Console.WriteLine($"🔍 Exception Type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"🔍 Inner Exception: {ex.InnerException.Message}");
            }
            return null;
        }
    }
}