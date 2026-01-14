---
inclusion: fileMatch
fileMatchPattern: '**/UpdateChecker*|**/TaskpaneManager*|**/ThisAddIn*'
---

# Update System

## CRITICAL: TLS Configuration Required
GitHub requires TLS 1.2+ but .NET Framework defaults to older versions.

```csharp
static UpdateChecker()
{
    // Force TLS 1.2 or higher for GitHub API compatibility
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
    httpClient.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");
}
```

## Non-Disruptive Update Flow
**Design Principle**: Download now, install on natural app shutdown

### Update Process
1. **User Prompt**: "גרסה חדשה זמינה... האם ברצונך להוריד ולהתקין?"
2. **Download**: Progress window shows download from GitHub releases
3. **Progress Window Closes**: Wait for actual window closure
4. **Final Confirmation**: "העדכון הורד בהצלחה! ההתקנה תתבצע כאשר תסגור את Word. האם ברצונך להמשיך?"
   - **OK**: Store installer path for deferred execution
   - **Cancel**: Delete installer file, clear pending path
5. **Deferred Installation**: When Word closes, run installer with `--silent`

## Key Components

### UpdateChecker.cs
- `PendingInstallerPath` - Static property stores installer for deferred execution
- `RunPendingInstaller()` - Called during app shutdown
- `CheckAndPromptForUpdateAsync()` - Main update flow with Hebrew UI

### ThisAddIn.cs
```csharp
private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
{
    // Run any pending installer that was deferred during update process
    UpdateChecker.RunPendingInstaller();
}
```

### TaskpaneManager.cs
- Triggers update check when user opens taskpanes (not on startup)
- Uses Word-specific quit: `Globals.ThisAddIn.Application.Quit()`

## Silent Installation Integration
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = PendingInstallerPath,
    Arguments = "--silent",
    UseShellExecute = true,
    Verb = "runas"
});
```

## GitHub Integration
- **API Endpoint**: `https://api.github.com/repos/KleiKodesh/KleiKodeshProject/releases/latest`
- **Download Pattern**: `https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{version}/KleiKodeshSetup-{version}.exe`
- **Temp Storage**: `Path.GetTempPath()` for downloaded installer
- **Cleanup**: Delete installer after execution or cancellation

## User Experience Benefits
- **Zero Workflow Disruption**: User continues working immediately after download
- **Natural Installation Timing**: Happens during normal app closure
- **Final Control**: User can cancel even after download completes
- **Silent Completion**: No UI during actual installation