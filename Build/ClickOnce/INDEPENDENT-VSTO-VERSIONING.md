# Independent VSTO Version Management

## Summary

The ClickOnce build script now automatically increments the VSTO project version independently from the WPF installer version, as requested.

## How It Works

### Version Separation
- **WPF Installer Version**: `v1.0.88` (in `InstallProgressWindow.xaml.cs`)
  - Used for GitHub releases and update system
  - Only incremented when `-NoVersionIncrement` is NOT specified
  - Remains stable for update compatibility

- **VSTO Project Version**: `1.0.89.0` (auto-incremented)
  - Used for ClickOnce deployment identity
  - **Always incremented** on every build (regardless of `-NoVersionIncrement`)
  - Independent versioning prevents deployment conflicts

### Auto-Increment Logic

The script automatically:

1. **Reads current VSTO version** from `KleiKodeshVsto.csproj`:
   ```xml
   <ApplicationVersion>1.0.88.0</ApplicationVersion>
   ```

2. **Increments patch number**: `1.0.88.0` → `1.0.89.0`

3. **Updates project file** with new version

4. **Updates AssemblyInfo.cs** to match:
   ```csharp
   [assembly: AssemblyVersion("1.0.89.0")]
   [assembly: AssemblyFileVersion("1.0.89.0")]
   ```

5. **Uses VSTO version** for ClickOnce deployment and installer naming

## Build Output

**Current Build Results**:
- WPF Installer Version: `v1.0.88` (unchanged)
- VSTO Project Version: `1.0.89.0` (auto-incremented)
- Generated Installer: `KleiKodeshClickOnce-1.0.89.0.exe`
- ClickOnce Application: `KleiKodesh_1_0_89_0`

## Benefits

### 1. No Deployment Conflicts
Each ClickOnce build gets a unique version, preventing "application with same identity" errors.

### 2. Independent Development
- VSTO version increments with each build
- WPF installer version remains stable for releases
- No need to manually manage VSTO versions

### 3. Automatic Process
- No manual intervention required
- Consistent versioning across all VSTO components
- Proper ClickOnce manifest generation

## Usage

### Normal Build (Auto-increment VSTO)
```powershell
.\build-clickonce.ps1 -Platform AnyCPU
```
- Increments WPF installer version (if not `-NoVersionIncrement`)
- **Always** increments VSTO version
- Creates installer with VSTO version number

### Skip WPF Version Increment
```powershell
.\build-clickonce.ps1 -Platform AnyCPU -NoVersionIncrement
```
- Keeps WPF installer version unchanged
- **Still** increments VSTO version
- Useful for testing without affecting release versions

## File Locations

### VSTO Project Version
- **Project File**: `KleiKodeshVsto/KleiKodeshVsto.csproj`
  - `<ApplicationVersion>1.0.89.0</ApplicationVersion>`
- **Assembly Info**: `KleiKodeshVsto/Properties/AssemblyInfo.cs`
  - `[assembly: AssemblyVersion("1.0.89.0")]`
  - `[assembly: AssemblyFileVersion("1.0.89.0")]`

### WPF Installer Version
- **Version Source**: `KleiKodeshVstoInstallerWpf/InstallProgressWindow.xaml.cs`
  - `const string Version = "v1.0.88";`

## Next Build

The next time you run the ClickOnce build:
- VSTO version will increment to `1.0.90.0`
- WPF installer version stays at `v1.0.88` (unless you increment it)
- New installer: `KleiKodeshClickOnce-1.0.90.0.exe`

This ensures each ClickOnce build is unique while keeping the main project version stable.