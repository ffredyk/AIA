# AIA Mobile - Quick Start Guide

## Prerequisites

- Visual Studio 2022 (version 17.12 or later)
- .NET 10 SDK installed
- Android SDK installed (via Visual Studio Installer)
- Android emulator configured OR physical Android device

## Building the Project

### Option 1: Visual Studio

1. Open `AIA.sln` in Visual Studio 2022
2. Right-click on `AIA.Mobile` project ? Set as Startup Project
3. Select target:
   - Android Emulator (e.g., Pixel 5 - API 34)
   - OR connected Android device (enable USB debugging)
4. Press **F5** to build and run

### Option 2: Command Line

```bash
# Navigate to solution root
cd D:\dev\C#\AIA

# Restore packages
dotnet restore

# Build the mobile project
cd AIA.Mobile
dotnet build -f net10.0-android

# Run on emulator (requires emulator running)
dotnet run -f net10.0-android
```

## Project Structure

```
AIA/
??? AIA.csproj                  # Desktop WPF app
??? AIA.Domain/                 # Shared models
?   ??? Models/
?       ??? TaskItem.cs
?       ??? ReminderItem.cs
?       ??? DataBankModels.cs
?       ??? ChatModels.cs
?       ??? AIModels.cs
?       ??? SyncModels.cs
??? AIA.Mobile/                 # MAUI mobile app
?   ??? Data/
?   ?   ??? AiaDatabase.cs      # SQLite DB
?   ??? Pages/
?   ?   ??? TasksPage.xaml      # ? Working
?   ?   ??? [Others]            # ? Placeholder
?   ??? ViewModels/
?   ??? MauiProgram.cs          # DI setup
??? Plugins/                    # Desktop plugins
```

## First Launch

When you run the app for the first time:

1. **Bottom navigation** appears with 5 tabs
2. Navigate to **Tasks** tab
3. Tap the **+** button (bottom-right)
4. Enter a task title
5. View the task card in the list

## Database Location

SQLite database is created at:
```
{AppDataDirectory}/aia.db3
```

On Android emulator:
```
/data/data/com.aia.mobile/files/aia.db3
```

To view/edit database:
- Use Android Device Monitor
- Or pull file via ADB: `adb pull /data/data/com.aia.mobile/files/aia.db3`

## Testing Features

### Tasks Page (Functional)
? Add task (FAB button)  
? View task list  
? Delete task (swipe left)  
? Tap task (opens edit dialog - temporary)  
? Pull-to-refresh  
? Dark mode (system-based)  

### Other Pages (Placeholder)
- Show "Coming Soon" message
- Navigation works
- Ready for implementation

## Troubleshooting

### Build Errors

**Error: Cannot resolve 'AIA.Domain'**
- Solution: Clean and rebuild all projects
- `dotnet clean && dotnet build`

**Error: SQLite not found**
- Solution: Restore NuGet packages
- Right-click solution ? Restore NuGet Packages

### Runtime Errors

**App crashes on launch**
- Check Android version (requires API 21+)
- Check logs in Output window (Android Device Log)

**Database errors**
- Delete app from emulator and reinstall
- Fresh database will be created

### Dark Mode Not Working
- Check device Settings ? Display ? Dark theme
- App follows system setting automatically

## Development Tips

1. **Hot Reload**: Edit XAML while app is running, changes apply instantly
2. **Breakpoints**: Set in ViewModels to debug logic
3. **XAML Live Preview**: Open XAML files to see design preview
4. **Device Log**: View ? Other Windows ? Android Device Log

## Next Steps

Implement the remaining pages:

1. **RemindersPage**: Copy structure from TasksPage
2. **DataBanksPage**: Similar list-detail pattern
3. **ChatPage**: Message list + input field
4. **SettingsPage**: Form layout with preferences

See `IMPLEMENTATION.md` for detailed roadmap.

## Useful Commands

```bash
# List connected devices/emulators
adb devices

# View app logs
adb logcat | grep "AIA.Mobile"

# Clear app data
adb shell pm clear com.aia.mobile

# Uninstall app
adb uninstall com.aia.mobile

# Install APK
adb install path/to/app.apk
```

## Resources

- [.NET MAUI Documentation](https://learn.microsoft.com/en-us/dotnet/maui/)
- [SQLite-net Documentation](https://github.com/praeclarum/sqlite-net)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)

---

Happy coding! ??
