!define PRODUCT_NAME "KleiKodesh"
!define PRODUCT_VERSION "1.0.87.10"
!define PRODUCT_PUBLISHER "KleiKodesh"
!define PRODUCT_WEB_SITE "https://github.com/shoshiiran/KleiKodeshProject"

!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "KleiKodeshClickOnce-${PRODUCT_VERSION}.exe"
InstallDir "$APPDATA\${PRODUCT_NAME}\ClickOnceTemp"
RequestExecutionLevel user
ShowInstDetails show

; Set the installer icon (this is what shows in Windows Explorer)
Icon "..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico"

; Set the MUI icon for installer pages
!define MUI_ABORTWARNING
!define MUI_ICON "..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico"
!define MUI_UNICON "..\\..\\KleiKodeshVsto\\Resources\\KleiKodesh_Main.ico"

; Add version information to the executable
VIProductVersion "1.0.87.10"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} ClickOnce Installer"
VIAddVersionKey "FileVersion" "1.0.87.10"
VIAddVersionKey "LegalCopyright" "ֲ© ${PRODUCT_PUBLISHER}"

!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    
    ; Extract all ClickOnce files using directory-based approach
    File /r "C:\Users\Admin\source\KleiKodeshProject\Build\ClickOnce\ClickOnceOutput\*"
    
    ; Create extraction complete marker
    FileOpen $0 "$INSTDIR\extraction_complete.marker" w
    FileWrite $0 "ClickOnce files extracted successfully"
    FileClose $0
    
    ; Run ClickOnce setup
    DetailPrint "Starting ClickOnce installation..."
    
    ; Wait a moment to ensure all files are fully extracted
    Sleep 2000
    
    ; Verify setup.exe exists
    IfFileExists "$INSTDIR\setup.exe" SetupOK SetupError
    
    SetupError:
        DetailPrint "ERROR: setup.exe not found after extraction"
        MessageBox MB_ICONSTOP "ClickOnce setup.exe was not extracted properly. Please try again."
        Abort
    
    SetupOK:
        DetailPrint "setup.exe verified, checking application files..."
        
        ; Verify Application Files directory exists
        IfFileExists "$INSTDIR\Application Files\*.*" AppFilesOK AppFilesError
        
        AppFilesError:
            DetailPrint "ERROR: Application Files directory not found"
            MessageBox MB_ICONSTOP "ClickOnce application files were not extracted properly. Please try again."
            Abort
        
        AppFilesOK:
            DetailPrint "All critical files verified, proceeding with ClickOnce installation..."
            
            ; Run ClickOnce setup with proper error handling
            ExecWait '"$INSTDIR\setup.exe"' $1
    
    ; Check if installation was successful
    ${If} $1 == 0
        DetailPrint "ClickOnce installation completed successfully"
        
        ; Simple delayed cleanup - just wait and clean up
        DetailPrint "ClickOnce installation completed successfully"
        DetailPrint "Temporary files will remain for manual cleanup if needed"
        DetailPrint "Location: $INSTDIR"
        
        DetailPrint "Installation location: $INSTDIR"
    ${Else}
        DetailPrint "ClickOnce installation returned code: $1"
        
        ${If} $1 == -2147009290
            ; User cancelled installation
            DetailPrint "Installation was cancelled by user"
            MessageBox MB_ICONINFORMATION "Installation was cancelled."
        ${ElseIf} $1 == -2147009294
            ; Trust not granted
            DetailPrint "Installation failed: Trust not granted"
            MessageBox MB_ICONEXCLAMATION "Installation failed: Application trust was not granted.$\n$\nThis may be due to security settings or network restrictions."
        ${Else}
            DetailPrint "Installation failed with error code: $1"
            MessageBox MB_ICONEXCLAMATION "Installation may not have completed successfully.$\n$\nError code: $1$\n$\nPlease check Windows Event Log for more details."
        ${EndIf}
        
        ; Clean up immediately on failure, but with a small delay
        DetailPrint "Cleaning up temporary files..."
        Sleep 3000
        RMDir /r "$INSTDIR"
    ${EndIf}
SectionEnd

Function .onInit
    ; Check if .NET Framework 4.8 is installed
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
    ${If} $0 == ""
        MessageBox MB_YESNO|MB_ICONQUESTION ".NET Framework 4.8 is required but not detected. Continue anyway?" IDYES continue
        Abort
    continue:
    ${EndIf}
    
    ; Check if VSTO Runtime is available
    ReadRegStr $1 HKLM "SOFTWARE\Microsoft\VSTO Runtime Setup\v4R" "Version"
    ${If} $1 == ""
        MessageBox MB_YESNO|MB_ICONQUESTION "Visual Studio Tools for Office Runtime may not be installed. Continue anyway?" IDYES continue2
        Abort
    continue2:
    ${EndIf}
FunctionEnd

Function .onInstSuccess
    MessageBox MB_ICONINFORMATION "KleiKodesh has been installed successfully!$\n$\nThe add-in will be available in Microsoft Word."
FunctionEnd
