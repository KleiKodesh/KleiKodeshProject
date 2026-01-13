---
inclusion: fileMatch
fileMatchPattern: '**/UpdateChecker*|**/TaskpaneManager*|**/ThisAddIn*'
---

# Update System Implementation

## CRITICAL: TLS Configuration Required

### GitHub API Compatibility
**PROBLEM**: GitHub requires TLS 1.2+ but .NET Framework defaults to older TLS versions.
**SOLUTION**: Always set `ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;`

**IMPLEMENTATION**:
```csharp
using System.Net;

static UpdateChecker()
{
    // Force TLS 1.2 or higher for GitHub API compatibility
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
    httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
}
```

**APPLY TO**: All HttpClient instances that call GitHub API

## Update Flow Architecture

### Non-Disruptive Update Pattern
**DESIGN PRINCIPLE**: Download now, install on natural app shutdown

**FLOW**:
1. **User Prompt**: "גרסה חדשה זמינה... האם ברצונך להוריד ולהתקין?"
2. **Download**: Progress window shows download from GitHub releases
3. **Progress Window Closes**: Wait for actual window closure before next step
4. **Final Confirmation**: "העדכון הורד בהצלחה! ההתקנה תתבצע כאשר תסגור את Word. האם ברצונך להמשיך?"
   - **OK**: Store installer path for deferred execution
   - **Cancel**: Delete installer file, clear pending path
5. **Deferred Installation**: When Word closes, run installer silently

### Key Components

**UpdateChecker.cs**:
- `PendingInstallerPath` - Static property stores installer for deferred execution
- `RunPendingInstaller()` - Called during app shutdown, runs with `--silent` flag
- `CheckAndPromptForUpdateAsync()` - Main update flow with user prompts

**ThisAddIn.cs**:
- `ThisAddIn_Shutdown()` - Calls `UpdateChecker.RunPendingInstaller()`
- Ensures installer runs during clean app shutdown

**TaskpaneManager.cs**:
- Triggers update check when user opens taskpanes
- Uses Word-specific quit method: `Globals.ThisAddIn.Application.Quit()`

## Silent Installation Integration

### Installer Silent Mode
**ARGUMENTS**: `--silent` or `/silent`
**BEHAVIOR**: Skips MainWindow, goes directly to InstallProgressWindow
**USAGE**: Perfect for deferred updates - no user interaction required

**IMPLEMENTATION**:
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = PendingInstallerPath,
    Arguments = "--silent",
    UseShellExecute = true,
    Verb = "runas"
});
```

## User Experience Principles

### Zero Workflow Disruption
- **Download when convenient**: User chooses when to start download
- **Install when natural**: Installation happens during normal app closure
- **No forced interruption**: User can continue working immediately after download
- **Final control**: User can cancel even after download completes

### Progressive Disclosure
- **Simple initial choice**: Just "download and install?" 
- **Clear progress**: Visual download progress window
- **Final confirmation**: Option to proceed or cancel after download
- **Silent completion**: No UI during actual installation

## Error Handling

### Network Issues
- **TLS errors**: Ensure TLS 1.2+ configuration is applied
- **GitHub API failures**: Graceful fallback, don't crash app
- **Download interruption**: Clean up partial files, allow retry

### Installation Issues
- **Deferred execution**: If immediate installer launch fails, defer to shutdown
- **Silent mode failures**: Log errors but don't show UI during shutdown
- **Cleanup**: Always clean up installer files after execution or cancellation

## Registry Integration

### Version Storage
- **Location**: `HKEY_CURRENT_USER\SOFTWARE\KleiKodesh\Version`
- **Format**: GitHub tag format (e.g., "v1.0.31")
- **Usage**: Compare with GitHub releases to detect updates

### Update Frequency
- **Trigger**: When user opens taskpanes (not on startup)
- **Frequency**: Controlled by existing update interval logic
- **Non-intrusive**: Only check when user is actively using features

## GitHub Integration

### API Endpoint
- **URL**: `https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest`
- **Headers**: User-Agent required for GitHub API
- **Response**: JSON with tag_name, download URLs, release info

### Download URL Pattern
- **Format**: `https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}.exe`
- **Temp Storage**: `Path.GetTempPath()` for downloaded installer
- **Cleanup**: Delete installer after execution or user cancellation

## Implementation Status ✅

**COMPLETED FEATURES**:
- ✅ TLS 1.2+ configuration for GitHub API compatibility
- ✅ Non-disruptive download and deferred installation flow
- ✅ Silent installer integration with `--silent` flag
- ✅ Progress window with proper closure synchronization
- ✅ User cancellation support with file cleanup
- ✅ VSTO shutdown integration for automatic installer execution
- ✅ Hebrew UI messages with proper RTL support
- ✅ Error handling for network and installation issues

**ARCHITECTURE BENEFITS**:
- **User-friendly**: No workflow interruption, natural installation timing
- **Reliable**: Handles network issues, partial downloads, and installation failures
- **Clean**: Proper file cleanup, no leftover installers
- **Flexible**: User maintains control throughout the process
- **Silent**: Final installation happens invisibly during app shutdown