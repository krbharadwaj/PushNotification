using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// Load secrets from config file
static Dictionary<string, string> LoadSecrets()
{
    var secrets = new Dictionary<string, string>();
    var configPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "SECRETS.config");
    
    if (File.Exists(configPath))
    {
        foreach (var line in File.ReadAllLines(configPath))
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
        Console.WriteLine($"✅ Loaded {secrets.Count} secrets from SECRETS.config");
    }
    else
    {
        Console.WriteLine($"⚠️ SECRETS.config not found at: {configPath}");
    }
    return secrets;
}

var secrets = LoadSecrets();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Azure AD Configuration - Loaded from SECRETS.config
var TenantId = secrets.GetValueOrDefault("TenantId") ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? "YOUR_TENANT_ID_HERE";
var ClientId = secrets.GetValueOrDefault("ClientId") ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? "YOUR_CLIENT_ID_HERE";
var ClientSecret = secrets.GetValueOrDefault("ClientSecret") ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? "YOUR_CLIENT_SECRET_HERE";

// Simple in-memory storage for device registrations
var registeredDevices = new Dictionary<string, DeviceInfo>();

// Get WNS Access Token
async Task<string?> GetWnsTokenAsync(HttpClient httpClient)
{
    try
    {
        var tokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
        
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("client_secret", ClientSecret),
            new KeyValuePair<string, string>("scope", "https://wns.windows.com/.default")
        });

        var response = await httpClient.PostAsync(tokenEndpoint, content);
        if (response.IsSuccessStatusCode)
        {
            var responseJson = await response.Content.ReadAsStringAsync();
            var tokenDoc = JsonDocument.Parse(responseJson);
            return tokenDoc.RootElement.GetProperty("access_token").GetString();
        }
        
        Console.WriteLine($"Token request failed: {response.StatusCode}");
        return null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error getting token: {ex.Message}");
        return null;
    }
}

// Send Push Notification
async Task<bool> SendPushNotificationAsync(HttpClient httpClient, string channelUri, string accessToken, string message, string? title = null)
{
    try
    {
        var payload = JsonSerializer.Serialize(new
        {
            message = message,
            title = title ?? "Notification",
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        });

        var request = new HttpRequestMessage(HttpMethod.Post, channelUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-WNS-Type", "wns/raw");
        request.Headers.Add("X-WNS-RequestForStatus", "true");
        
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        
        var response = await httpClient.SendAsync(request);
        
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine($"✅ Push notification sent successfully: {response.StatusCode}");
            return true;
        }
        else
        {
            var errorText = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"❌ Push notification failed: {response.StatusCode} - {errorText}");
            return false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Exception sending push: {ex.Message}");
        return false;
    }
}

// API Endpoints
app.MapGet("/", () => new { 
    service = "Simple Push Notification Server", 
    status = "running",
    timestamp = DateTime.UtcNow,
    registeredDevices = registeredDevices.Count
});

app.MapPost("/register", (RegisterRequest request) =>
{
    try
    {
        var device = new DeviceInfo(request.DeviceId, request.ChannelUri, request.UserId, DateTime.UtcNow);
        registeredDevices[request.DeviceId] = device;
        
        Console.WriteLine($"📱 Device registered: {request.DeviceId} for user {request.UserId}");
        
        return Results.Ok(new { 
            success = true, 
            message = "Device registered successfully",
            deviceId = request.DeviceId,
            registeredAt = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Registration error: {ex.Message}");
        return Results.BadRequest(new { success = false, message = ex.Message });
    }
});

app.MapPost("/send", async (PushRequest request, HttpClient httpClient) =>
{
    try
    {
        if (!registeredDevices.TryGetValue(request.DeviceId, out var device))
        {
            return Results.NotFound(new { success = false, message = "Device not found" });
        }

        Console.WriteLine($"📤 Sending push to device: {request.DeviceId}");
        Console.WriteLine($"📋 Message: {request.Message}");
        
        // Get WNS token
        var token = await GetWnsTokenAsync(httpClient);
        if (token == null)
        {
            return Results.Problem("Failed to get WNS access token");
        }

        // Send notification
        var success = await SendPushNotificationAsync(httpClient, device.ChannelUri, token, request.Message, request.Title);
        
        return Results.Ok(new { 
            success = success, 
            message = success ? "Notification sent successfully" : "Failed to send notification",
            sentAt = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Send error: {ex.Message}");
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/devices", () => Results.Ok(registeredDevices.Values.ToList()));

app.MapDelete("/devices/{deviceId}", (string deviceId) =>
{
    if (registeredDevices.Remove(deviceId))
    {
        Console.WriteLine($"🗑️ Device removed: {deviceId}");
        return Results.Ok(new { success = true, message = "Device removed" });
    }
    return Results.NotFound(new { success = false, message = "Device not found" });
});

Console.WriteLine("🚀 Simple Push Notification Server");
Console.WriteLine("📡 WNS Integration Ready");
Console.WriteLine($"🔑 Azure Tenant: {TenantId}");

app.Run("http://localhost:5000");

// Models
record DeviceInfo(string DeviceId, string ChannelUri, string UserId, DateTime RegisteredAt);
record RegisterRequest(string DeviceId, string ChannelUri, string UserId);
record PushRequest(string DeviceId, string Message, string? Title = null);