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

### Automatic Cleanup for Updates Only
**CRITICAL DISTINCTION**: Cleanup only happens for automatic updates, NOT manual installations

**UPDATE CLEANUP FLOW**:
1. **Deferred Update**: Installer runs via `RunPendingInstaller()` with `--silent`
2. **Cleanup Script Created**: Batch file monitors installer process completion
3. **Self-Deleting**: Both installer and cleanup script delete themselves
4. **Clean System**: No leftover files from automatic updates

**MANUAL INSTALLATION**: No cleanup - user retains control over installer file

**CLEANUP SCRIPT LOGIC**:
```batch
# Wait for installer process to finish
# Delete installer file (temp directory only)
# Self-delete cleanup script: (goto) 2>nul & del "%~f0"
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
- ✅ **Automatic cleanup for updates only** - self-deleting installer and cleanup script
- ✅ **Manual installation preservation** - no cleanup for user-downloaded installers
- ✅ **TESTED AND VERIFIED** - Self-deleting cleanup functionality confirmed working

**TEST RESULTS** ✅:
- **Process Monitoring**: Cleanup script correctly detects installer completion
- **File Deletion**: Installer successfully removed from temp directory
- **Self-Deletion**: Cleanup script removes itself using `(goto) 2>nul & del "%~f0"`
- **No Leftovers**: Both installer and cleanup script completely removed
- **Timing**: 5-second post-completion wait ensures clean deletion

**ARCHITECTURE BENEFITS**:
- **User-friendly**: No workflow interruption, natural installation timing
- **Reliable**: Handles network issues, partial downloads, and installation failures
- **Clean**: Proper file cleanup for updates, preserves manual installations
- **Flexible**: User maintains control throughout the process
- **Silent**: Final installation happens invisibly during app shutdown
- **Selective cleanup**: Only automatic updates clean up, manual installs preserved
- **Production ready**: All components tested and verified working

## Testing Framework

### Self-Deleting Cleanup Test
**Location**: `TestSilentCleanup/TestSilentCleanup.cs`
**Purpose**: Verify automatic cleanup functionality works correctly

**Test Process**:
1. Creates fake installer that simulates 10-second installation
2. Generates cleanup script using same logic as UpdateChecker
3. Runs both installer and cleanup script simultaneously
4. Verifies both files are completely removed after completion

**Test Command**:
```bash
dotnet build TestSilentCleanup/TestSilentCleanup.csproj
.\TestSilentCleanup\bin\Debug\net48\TestSilentCleanup.exe
```

**Expected Result**: "✅ SUCCESS: Both files were cleaned up!"

### Cleanup Script Logic Verification
**Self-Deletion Technique**: `(goto) 2>nul & del "%~f0"`
- Creates harmless error that doesn't affect execution
- Continues to delete command regardless of error
- Deletes currently running batch file from memory
- **VERIFIED WORKING**: Test confirms complete file removal

## Production Deployment Status

### Ready for Production ✅
**STATUS**: All components tested and verified working
**DEPLOYMENT**: System ready for immediate production use

**INTEGRATION POINTS**:
- `UpdateCheckerLib/UpdateChecker.cs` - Core update logic with TLS and cleanup
- `KleiKodeshVsto/ThisAddIn.cs` - Shutdown integration for deferred installation
- `KleiKodeshVsto/Helpers/TaskpaneManager.cs` - Update trigger when user opens features
- `KleiKodeshVstoInstallerWpf/App.xaml.cs` - Silent mode support with `--silent` flag

**USER EXPERIENCE FLOW**:
1. **Seamless Detection**: Updates checked when user opens taskpanes
2. **User Choice**: Simple Hebrew dialog for download confirmation
3. **Background Download**: Progress window with cancellation support
4. **Final Control**: User can cancel even after download completes
5. **Natural Installation**: Silent install during normal Word shutdown
6. **Clean System**: Automatic cleanup leaves no trace files

**RELIABILITY FEATURES**:
- TLS 1.2+ compatibility with GitHub API
- Robust error handling for network issues
- File cleanup on cancellation or completion
- Self-deleting cleanup scripts (tested and verified)
- Preservation of manual installations (no unwanted cleanup)