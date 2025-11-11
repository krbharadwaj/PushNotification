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

// Simple in-memory storage for channel registrations (no device ID needed)
var registeredChannels = new List<ChannelInfo>();

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
        // Create toast XML for background activation
        var toastTitle = System.Security.SecurityElement.Escape(title ?? "Notification");
        var toastMessage = System.Security.SecurityElement.Escape(message);
        
        var toastXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<toast launch=""app-defined-string"">
    <visual>
        <binding template=""ToastGeneric"">
            <text>{toastTitle}</text>
            <text>{toastMessage}</text>
        </binding>
    </visual>
</toast>";

        var request = new HttpRequestMessage(HttpMethod.Post, channelUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-WNS-Type", "wns/toast");
        request.Headers.Add("X-WNS-RequestForStatus", "true");
        
        request.Content = new StringContent(toastXml, Encoding.UTF8, "text/xml");
        
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
    registeredChannels = registeredChannels.Count
});

app.MapPost("/register", (RegisterRequest request) =>
{
    try
    {
        // Remove old channel for same user if exists
        registeredChannels.RemoveAll(c => c.UserId == request.UserId);
        
        var channel = new ChannelInfo(request.ChannelUri, request.UserId, DateTime.UtcNow);
        registeredChannels.Add(channel);
        
        Console.WriteLine($"📱 Channel registered for user: {request.UserId}");
        Console.WriteLine($"📋 Total channels: {registeredChannels.Count}");
        
        return Results.Ok(new { 
            success = true, 
            message = "Channel registered successfully",
            userId = request.UserId,
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
        if (registeredChannels.Count == 0)
        {
            return Results.NotFound(new { success = false, message = "No channels registered" });
        }

        Console.WriteLine($"📤 Broadcasting push to {registeredChannels.Count} channel(s)");
        Console.WriteLine($"📋 Title: {request.Title ?? "Notification"}");
        Console.WriteLine($"📋 Message: {request.Message}");
        
        // Get WNS token
        var token = await GetWnsTokenAsync(httpClient);
        if (token == null)
        {
            return Results.Problem("Failed to get WNS access token");
        }

        // Send to all registered channels
        int successCount = 0;
        foreach (var channel in registeredChannels.ToList())
        {
            var success = await SendPushNotificationAsync(httpClient, channel.ChannelUri, token, request.Message, request.Title);
            if (success) successCount++;
        }
        
        return Results.Ok(new { 
            success = successCount > 0, 
            message = $"Notification sent to {successCount}/{registeredChannels.Count} channel(s)",
            sentAt = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Send error: {ex.Message}");
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/channels", () => Results.Ok(registeredChannels.ToList()));

app.MapDelete("/channels/{userId}", (string userId) =>
{
    var removed = registeredChannels.RemoveAll(c => c.UserId == userId);
    if (removed > 0)
    {
        Console.WriteLine($"🗑️ Channel removed for user: {userId}");
        return Results.Ok(new { success = true, message = "Channel removed" });
    }
    return Results.NotFound(new { success = false, message = "Channel not found" });
});

Console.WriteLine("🚀 Simple Push Notification Server");
Console.WriteLine("📡 WNS Integration Ready");
Console.WriteLine($"🔑 Azure Tenant: {TenantId}");

app.Run("http://localhost:5000");

// Models
record ChannelInfo(string ChannelUri, string UserId, DateTime RegisteredAt);
record RegisterRequest(string ChannelUri, string UserId);
record PushRequest(string Message, string? Title = null);