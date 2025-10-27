# Simple Push Notification Server

A lightweight ASP.NET Core server for sending push notifications to WinUI3 applications via Windows Notification Service (WNS).

## Features

- 🚀 Simple REST API for push notifications
- 📱 Device registration management
- 🔐 Azure AD authentication with WNS
- 📋 Swagger UI for API testing
- 💾 In-memory device storage

## Quick Start

### 1. Start the Server
```bash
cd SimplePushServer
dotnet run
```
Server runs on: http://localhost:5000

### 2. API Endpoints

#### Health Check
```
GET /
```

#### Register Device
```
POST /register
{
  "deviceId": "unique-device-id",
  "channelUri": "wns-channel-uri-from-winui3-app",
  "userId": "user-identifier"
}
```

#### Send Notification
```
POST /send
{
  "deviceId": "unique-device-id",
  "message": "Your notification message",
  "title": "Optional Title"
}
```

#### List Devices
```
GET /devices
```

#### Remove Device
```
DELETE /devices/{deviceId}
```

## Usage with WinUI3 App

1. **Start both applications:**
   - Run this server: `dotnet run` (from SimplePushServer folder)
   - Run WinUI3 app: Build and run WinUI3AppForWNSTest

2. **Get Channel URI from WinUI3 app:**
   - Click "Initialize Push" in the WinUI3 app
   - Copy the Channel URI from the app's output

3. **Register the device:**
   ```bash
   curl -X POST http://localhost:5000/register \
     -H "Content-Type: application/json" \
     -d '{
       "deviceId": "mydevice1", 
       "channelUri": "PASTE_CHANNEL_URI_HERE",
       "userId": "testuser"
     }'
   ```

4. **Send a test notification:**
   ```bash
   curl -X POST http://localhost:5000/send \
     -H "Content-Type: application/json" \
     -d '{
       "deviceId": "mydevice1",
       "message": "Hello from server!",
       "title": "Test Notification"
     }'
   ```

## Configuration

The server is pre-configured with Azure AD credentials from the WinUI3 app:
- Tenant ID: 89d6c5fb-a1a0-4313-8cad-751efd188a0a
- Client ID: 9c959ee1-3eb8-4cfa-a528-4a04331dbdd9

## API Testing

Visit http://localhost:5000/swagger when the server is running for interactive API documentation.