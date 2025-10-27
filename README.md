# WinUI3 Push Notifications Demo

A complete solution demonstrating Windows push notifications using WinUI3 and Windows Notification Service (WNS).

## Features

- ✅ **Complete Push Notification Implementation** - Following Microsoft's official Windows App SDK pattern
- 🔧 **Built-in Troubleshooting** - Diagnostic tools to identify common configuration issues
- 📱 **Real-time Testing** - Send and receive push notifications within the app
- 🔍 **Comprehensive Logging** - Detailed status updates and error reporting
- 🎯 **Azure AD Integration** - Full OAuth2 flow for WNS access tokens

## Prerequisites

- Windows 11 or Windows 10 version 1809 (build 17763) or higher
- .NET 8.0 or later
- Visual Studio 2022 with Windows App SDK workload
- Azure Active Directory app registration with WNS permissions

## Azure Setup Required

### 1. Create Azure AD App Registration
1. Go to [Azure Portal](https://portal.azure.com) → Azure Active Directory → App Registrations
2. Create a new registration with multi-tenant support
3. Note down the **Application (client) ID** and **Directory (tenant) ID**
4. Create a client secret and note it down

### 2. Get the Enterprise Application Object ID
1. Go to Azure Active Directory → Enterprise Applications
2. Find your app registration
3. Copy the **Object ID** (this is different from App Registration Object ID)

### 3. Configure WNS Permissions
1. In your app registration, go to API Permissions
2. Add `https://wns.windows.com/.default` scope

### 4. Map Package Family Name (for packaged apps)
- Email `Win_App_SDK_Push@microsoft.com` with:
  - Subject: "Windows App SDK Push Notifications Mapping Request"
  - Body: "PFN: [your app's PFN], AppId: [your Azure App ID], ObjectId: [your Object ID]"

## Configuration

Update the following values in `PushManager.cs`:

```csharp
// Replace with your actual Azure AD values
private static readonly Guid AzureObjectId = new Guid("YOUR_ENTERPRISE_APP_OBJECT_ID");
private static readonly Guid AzureAppId = new Guid("YOUR_AZURE_APP_ID");
private const string TenantId = "YOUR_TENANT_ID";
private const string ClientSecret = "YOUR_CLIENT_SECRET";
```

## Building and Running

```bash
# Build the project
dotnet build WinUI3AppForWNSTest\WinUI3AppForWNSTest.csproj -r win-x64

# Run from Visual Studio or
dotnet run --project WinUI3AppForWNSTest\WinUI3AppForWNSTest.csproj
```

## Usage

1. **Initialize Push Notifications** - Click to set up the complete push system
2. **Troubleshoot** - Diagnose configuration issues and Azure setup
3. **Send Test Push** - Send a test notification to verify the complete flow

## Troubleshooting Common Issues

### BadRequest Error (400)
- **Most Common**: Using placeholder Azure Object ID
- **Solution**: Replace with actual Enterprise Application Object ID from Azure Portal

### Push Notifications Not Supported
- **Cause**: App is unpackaged or missing Windows App SDK
- **Solution**: Package as MSIX or install Windows App SDK system-wide

### Channel Creation Failed
- **Cause**: Incorrect Azure Object ID or missing PFN mapping
- **Solution**: Verify Object ID and complete PFN mapping process

## Architecture

- **PushManager.cs** - Core push notification logic following Microsoft's official pattern
- **MainWindow.xaml/cs** - UI for testing and demonstration
- **Package.appxmanifest** - COM server registration for background activation

## Key Implementation Details

- Event handlers registered before `PushNotificationManager.Register()` call
- Proper background push activation handling with deferral pattern
- Comprehensive error reporting with WNS-specific status headers
- Channel URI management with expiration tracking

## Resources

- [Windows App SDK Push Notifications Documentation](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/push-notifications/push-quickstart)
- [Azure AD App Registration Guide](https://learn.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)
- [WNS Overview](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/push-notifications/)

## License

This project is provided as a sample for educational and testing purposes.