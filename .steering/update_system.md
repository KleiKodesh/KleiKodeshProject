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
    // Create launcher script that waits for Word to fully close before running installer
    UpdateChecker.RunPendingInstaller();
}
```

### TaskpaneManager.cs
- Triggers update check when user opens taskpanes (not on startup)
- Uses Word-specific quit: `Globals.ThisAddIn.Application.Quit()`

## Deferred Installation with Process Monitoring
The system creates a launcher script that:
1. **Waits for Word to fully close** - Monitors WINWORD.EXE process
2. **Releases all resources** - Additional delay after process ends
3. **Runs installer silently** - Uses PowerShell with elevated privileges
4. **Cleans up** - Removes installer file and self-deletes script

```batch
# Launcher script waits for Word process to end
:waitForWord
tasklist /FI "IMAGENAME eq WINWORD.EXE" | find /I "WINWORD.EXE"
if ERRORLEVEL 0 (timeout /t 2 & goto waitForWord)

# Then runs installer with elevation
powershell Start-Process -FilePath 'installer.exe' -ArgumentList '--silent' -Verb RunAs
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