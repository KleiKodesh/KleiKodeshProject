# KleiKodesh VSTO Installer

A WPF-based installer for the KleiKodesh Microsoft Word add-in, designed to handle multi-user installations with automatic trust configuration.

## Overview

This installer deploys a VSTO (Visual Studio Tools for Office) add-in to Microsoft Word with the following key features:

- **Multi-user support**: Works for any user account on the machine
- **Automatic trust configuration**: No security prompts or manual certificate installation
- **Future-proof**: Supports updates and version changes
- **Clean installation**: Removes old versions automatically
- **User-friendly**: Hebrew interface with progress indication

## Technical Specifications

### Installation Process

1. **Pre-installation Cleanup**
   - Detects and removes old installations from Program Files locations
   - Cleans up obsolete registry entries from previous versions
   - Closes Microsoft Word if running (with user confirmation)

2. **File Deployment**
   - Extracts VSTO files to `%LOCALAPPDATA%\KleiKodesh\`
   - Supports any ZIP structure and .vsto filename
   - Recursive extraction with directory structure preservation

3. **Registry Configuration**
   - Creates required Office add-in registry entries in `HKEY_CURRENT_USER`
   - Registers in both `Addins` and `AddinsData` registry paths
   - Sets proper LoadBehavior for automatic loading

4. **Trust Configuration**
   - Adds solution to Office inclusion list for immediate trust
   - Configures trusted folder for future versions
   - Extracts public key dynamically from manifest

### Registry Entries Created

#### Add-in Registration
```
HKEY_CURRENT_USER\Software\Microsoft\Office\Word\Addins\KleiKodesh
├── Description (REG_SZ): "כלי קודש"
├── FriendlyName (REG_SZ): "כלי קודש"
├── LoadBehavior (REG_DWORD): 3
└── Manifest (REG_SZ): "file:///[InstallPath]\[VstoFile]|vstolocal"

HKEY_CURRENT_USER\Software\Microsoft\Office\Word\AddinsData\KleiKodesh
├── Description (REG_SZ): "כלי קודש"
├── FriendlyName (REG_SZ): "כלי קודש"
├── LoadBehavior (REG_DWORD): 3
└── Manifest (REG_SZ): "file:///[InstallPath]\[VstoFile]|vstolocal"
```

#### Trust Configuration
```
HKEY_CURRENT_USER\SOFTWARE\Microsoft\VSTO\Security\Inclusion\[Base64Key]
├── Url (REG_SZ): "file:///[VstoPath]|vstolocal"
├── PublicKey (REG_SZ): "[ExtractedPublicKey]"
└── AllowsUnsafeCode (REG_DWORD): 0

HKEY_CURRENT_USER\SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\[Base64Key]
├── Path (REG_SZ): "[InstallPath]"
└── AllowSubfolders (REG_DWORD): 1
```

#### Version Tracking
```
HKEY_CURRENT_USER\SOFTWARE\KleiKodesh
└── Version (REG_SZ): "v1.0.23"
```

### File Structure

```
%LOCALAPPDATA%\KleiKodesh\
├── KleiKodesh.vsto              # VSTO deployment manifest
├── KleiKodesh.dll.manifest      # Application manifest
├── KleiKodesh.dll               # Main add-in assembly
└── [Additional dependencies]    # Supporting files and libraries
```

## Features

### Multi-User Support
- **Per-user installation**: Each user gets their own copy in their AppData
- **No admin rights required**: Installs to user-accessible locations
- **Independent configurations**: Users can have different settings

### Automatic Trust Management
- **Office Inclusion List**: Adds solution to trusted solutions list
- **Trusted Folder**: Marks installation directory as trusted for future versions
- **Dynamic Public Key**: Extracts actual public key from VSTO manifest
- **No certificate installation**: Uses Office's built-in trust mechanisms

### Future-Proof Design
- **Dynamic .vsto discovery**: Finds any .vsto file regardless of name
- **Flexible file structure**: Supports changes in ZIP organization
- **Version independence**: Trust settings work across updates
- **Certificate resilience**: Handles certificate changes gracefully

### Clean Installation
- **Old version cleanup**: Removes previous installations automatically
- **Registry cleanup**: Cleans obsolete registry entries
- **Process management**: Handles running Word instances safely

## Build Requirements

### Prerequisites
- .NET 8.0 Windows Desktop Runtime
- WPF support
- Visual Studio 2022 (for building)

### Dependencies
- Microsoft.Win32.Registry (via .NET Framework)
- System.IO.Compression (built-in)
- Windows Presentation Foundation

### Build Process
1. **VSTO Project Build**: Automatically builds the KleiKodeshVsto project
2. **Package Creation**: Creates ZIP package from VSTO output
3. **Resource Embedding**: Embeds ZIP as embedded resource
4. **Installer Compilation**: Builds WPF installer with embedded resources

## Configuration

### Constants (InstallProgressWindow.xaml.cs)
```csharp
const string AppName = "KleiKodesh";
const string AppDisplayName = "כלי קודש";
const string Version = "v1.0.23";
const string InstallFolderName = "KleiKodesh";
const string ZipResourceName = "KleiKodesh.zip";
```

### Paths
- **Install Location**: `%LOCALAPPDATA%\KleiKodesh\`
- **Registry Base**: `Software\Microsoft\Office\Word\`
- **Trust Registry**: `SOFTWARE\Microsoft\VSTO\Security\`

## Error Handling

### Graceful Degradation
- Installation continues even if trust configuration fails
- Registry errors don't abort installation
- File extraction errors are handled appropriately

### User Communication
- Hebrew error messages for user-facing issues
- Progress indication during installation
- Confirmation dialogs for critical actions

### Exit Codes
- **0**: Successful installation
- **1**: Installation failed or user cancelled

## Security Considerations

### Trust Model
- Uses Office's built-in trust mechanisms
- No elevation of privileges required
- Per-user trust boundaries respected

### Certificate Handling
- Self-signed certificates supported
- Dynamic public key extraction
- No certificate installation to system stores

### File System Security
- Installs to user-accessible locations only
- Respects Windows file system permissions
- No modification of system directories

## Troubleshooting

### Common Issues

1. **Add-in not appearing in Word**
   - Check registry entries exist
   - Verify .vsto file is accessible
   - Confirm trust configuration

2. **Security warnings**
   - Ensure inclusion list entries are correct
   - Verify trusted folder configuration
   - Check public key matches manifest

3. **Installation fails**
   - Ensure Word is closed during installation
   - Check user has write access to AppData
   - Verify .NET runtime is installed

### Debug Information
- Version information stored in registry
- Installation path recorded in registry entries
- Trust configuration can be verified in Office security settings

## Compatibility

### Office Versions
- Microsoft Word 2016 or later
- Office 365 / Microsoft 365
- Both 32-bit and 64-bit Office installations

### Windows Versions
- Windows 10 or later
- Both x86 and x64 architectures
- Standard user accounts (no admin required)

### .NET Requirements
- .NET Framework 4.8 (for VSTO runtime)
- .NET 8.0 Windows Desktop (for installer)

## Development Notes

### Architecture
- **WPF Application**: Modern Windows desktop application
- **Embedded Resources**: VSTO package embedded in installer
- **Registry-based**: Uses Windows registry for configuration
- **Async Operations**: Non-blocking UI during installation

### Extensibility
- Easy to modify for different VSTO projects
- Configurable through constants
- Modular trust configuration
- Pluggable cleanup mechanisms

### Maintenance
- Version updates require only constant changes
- Trust configuration is automatic
- Registry paths are centralized
- Error handling is comprehensive

## License

This installer is designed specifically for the KleiKodesh VSTO add-in project.