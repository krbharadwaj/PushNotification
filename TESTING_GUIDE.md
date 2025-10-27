# Background Push Notification Testing Guide

## 🎯 Testing Scenario: Background App Activation

This demonstrates the key Windows push notification feature where a **closed/killed app gets reactivated** by incoming push notifications.

## 📋 Step-by-Step Testing Process:

### **1. Start SimplePushServer**
```bash
# Option A: From Visual Studio
- Open WinUI3AppForWNSTest.sln in Visual Studio
- Right-click "SimplePushServer" → Set as StartUp Project  
- Press F5 or Ctrl+F5

# Option B: From Terminal
cd SimplePushServer
dotnet run
```
**Expected:** Server starts on http://localhost:5000 with "✅ Loaded 4 secrets from SECRETS.config"

### **2. Launch WinUI3 App**
```bash
# Option A: From Visual Studio  
- Right-click "WinUI3AppForWNSTest" → Set as StartUp Project
- Press F5 or Ctrl+F5

# Option B: From Terminal
cd WinUI3AppForWNSTest
dotnet run --arch x64
```

### **3. Initialize Push Notifications**
- Click **"Initialize Push Notifications"** button
- Wait for "✅ Push notification initialization completed successfully!"
- **Green "Register with Server" button** should appear

### **4. Register Device with Server**
- Click **"Register with Server"** button  
- Wait for "✅ Device registered successfully!"
- App shows: "READY FOR BACKGROUND TESTING"

### **5. Close WinUI3 App Completely**
- **Important:** Close the app completely (X button or Alt+F4)
- App should NOT be running in taskbar or background

### **6. Send Push Notification from Server**
```powershell
# Send test notification
$notification = @{
    deviceId = "winui3-device"
    message = "Background activation test!"
    title = "Test Notification"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/send" -Method POST -Body $notification -ContentType "application/json"
```

### **7. Verify Background Activation**
**Expected Results:**
- ✅ SimplePushServer shows "✅ Push notification sent successfully"  
- ✅ WinUI3 app **automatically launches in background**
- ✅ App processes the notification and logs activation
- ✅ Demonstrates Windows background activation capability

## 🔧 API Endpoints for Testing:

### Register Device
```http
POST http://localhost:5000/register
{
  "deviceId": "winui3-device",
  "channelUri": "wns-channel-uri-from-app", 
  "userId": "testuser"
}
```

### Send Notification  
```http
POST http://localhost:5000/send
{
  "deviceId": "winui3-device",
  "message": "Your message here",
  "title": "Optional title"
}
```

### List Registered Devices
```http
GET http://localhost:5000/devices
```

## 🚀 What This Demonstrates:

1. **Real Windows Push Notifications** - Uses actual WNS service
2. **Background App Activation** - Closed apps get reactivated  
3. **Production-Ready Pattern** - Follows Microsoft's official implementation
4. **End-to-End Flow** - Complete client-server push notification system

## 📱 Key Features Tested:

- ✅ **Channel URI Generation** - WinUI3 app gets unique push channel
- ✅ **Device Registration** - Server stores channel for targeting  
- ✅ **Background Activation** - Killed apps restart via push notifications
- ✅ **Azure AD Integration** - Real authentication with WNS
- ✅ **Error Handling** - Comprehensive logging and troubleshooting