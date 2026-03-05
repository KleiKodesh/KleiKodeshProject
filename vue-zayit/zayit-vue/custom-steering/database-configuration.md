# Database Configuration Guidelines

This guide covers database path configuration functionality in the Zayit application.

## Overview

The database configuration system allows users to change the SQLite database location in the C# version of the application. This functionality is only available when running in WebView mode (C# wrapper), not in the standalone Vue development version.

## Architecture

### Frontend (Vue)

- **Settings Page**: Contains database path configuration UI
- **Conditional Display**: Only shows database path option when `webviewBridge.isAvailable()` returns true
- **Reactive Store**: Database path is stored in `settingsStore` and persisted to localStorage
- **Auto-reload**: Page automatically reloads after database path changes

### Backend (C#)

- **VB.NET Settings**: Uses `Interaction.SaveSetting()` and `Interaction.GetSetting()` for persistence
- **Registry Storage**: Settings stored in `HKEY_CURRENT_USER\Software\VB and VBA Program Settings\ZayitApp\Database`
- **Default Path**: `%AppData%\Roaming\io.github.kdroidfilter.seforimapp\databases\seforim.db`

## Key Components

### Vue Components

- **SettingsPage.vue**: Main settings interface with database path configuration
- **Conditional rendering**: `v-if="webviewBridge.isAvailable()"` for C# mode only

### C# Classes

- **DbQueries.cs**: Handles database path persistence and loading
- **ServiceProvider.cs**: Exposes database configuration methods to WebView bridge
- **WebViewBridgeService.cs**: Handles communication between Vue and C#

## Implementation Details

### Database Path Setting

```typescript
const selectDatabaseFile = async () => {
  const result = await webviewBridge.openDatabaseFilePicker();
  if (result.filePath) {
    databasePath.value = result.filePath;
    const success = await webviewBridge.setDatabasePath(result.filePath);
    if (success) {
      window.location.reload(); // Reload to use new database
    }
  }
};
```

### Database Path Reset

```typescript
const resetSettings = async () => {
  settingsStore.reset();
  if (webviewBridge.isAvailable()) {
    const success = await webviewBridge.setDatabasePath(""); // Clear path
    if (success) {
      window.location.reload(); // Reload to use default database
    }
  }
};
```

### C# Persistence

```csharp
public static void SetCustomDatabasePath(string path)
{
    if (string.IsNullOrEmpty(path))
    {
        ClearCustomDatabasePath();
        return;
    }

    _customDatabasePath = path;
    Interaction.SaveSetting("ZayitApp", "Database", "Path", path);
}
```

## Critical Requirements

### Page Reload Necessity

- **Database changes**: Must reload page after changing database path
- **Settings reset**: Must reload page after resetting settings
- **Reason**: Ensures all components use the new database connection

### Environment Detection

- **C# Mode**: Show database path option when `webviewBridge.isAvailable()` is true
- **Dev Mode**: Hide database path option completely
- **Reason**: Database path only relevant in C# wrapper, not standalone Vue

### Persistence Strategy

- **Frontend**: Store in Vue settings store + localStorage
- **Backend**: Store in VB.NET settings (Windows Registry)
- **Default handling**: Empty string clears custom path, reverts to default

## Error Handling

### File Picker Errors

```typescript
try {
  const result = await webviewBridge.openDatabaseFilePicker();
  // Handle result
} catch (error) {
  console.error("Failed to select database file:", error);
  // Continue gracefully, don't break UI
}
```

### Path Setting Errors

```csharp
try {
    Interaction.SaveSetting("ZayitApp", "Database", "Path", path);
} catch (Exception ex) {
    Console.WriteLine($"Failed to persist database path: {ex.Message}");
    // Log error but don't throw - graceful degradation
}
```

## UI Guidelines

### Settings Page Layout

- **Conditional display**: Only show in C# mode
- **File input**: Read-only input with browse button
- **Browse button**: Folder icon (📁) for file picker
- **Reset behavior**: Clear database path when resetting all settings

### User Experience

- **No alerts**: Don't show browser alerts for successful operations
- **Immediate feedback**: Settings changes visible immediately in UI
- **Auto-reload**: Page reloads automatically after database changes
- **Graceful fallback**: Continue working even if persistence fails

## Testing Considerations

### Development Mode

- Database path option should be completely hidden
- Settings reset should work without database path logic
- No WebView bridge calls should be made

### C# Mode

- Database path option should be visible and functional
- File picker should open native Windows dialog
- Path changes should persist across application restarts
- Settings reset should clear database path and reload page

## Common Issues

### Path Not Persisting

- **Cause**: VB.NET settings not being saved properly
- **Solution**: Check `Interaction.SaveSetting()` calls and error handling

### Page Not Reloading

- **Cause**: Missing `window.location.reload()` after database changes
- **Solution**: Always reload after successful database path changes

### Option Not Showing

- **Cause**: `webviewBridge.isAvailable()` returning false
- **Solution**: Verify WebView2 environment and bridge initialization

## Best Practices

1. **Always reload** after database path changes
2. **Check success** before reloading to avoid unnecessary reloads
3. **Handle errors gracefully** without breaking the UI
4. **Use conditional rendering** to show/hide based on environment
5. **Persist to both** frontend store and backend settings
6. **Clear properly** by setting empty string, not null
7. **Test in both modes** - development and C# wrapper
