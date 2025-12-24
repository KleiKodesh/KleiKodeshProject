; KleiKodesh Simple NSIS Wrapper
; Purpose: Check .NET dependencies, run WPF installer, and provide uninstaller

; Set Hebrew language and RTL support
!define MUI_LANGDLL_ALLLANGUAGES
!include "MUI2.nsh"

!define PRODUCT_NAME "כלי קודש"
; Version is now passed as a parameter from build script
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "v1.0.0"  ; Fallback version if not provided
!endif
!define PRODUCT_PUBLISHER "צוות כלי קודש"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"
!define DOTNET_VERSION "4.8"
!define DOTNET_REGKEY "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"

!include "LogicLib.nsh"
!include "FileFunc.nsh"

!insertmacro GetParameters

; Set Hebrew language
!insertmacro MUI_LANGUAGE "Hebrew"

; Classic NSIS UI for minimal window
Icon "..\KleiKodeshInstallerWpf\KleiKodesh_Main.ico"
UninstallIcon "..\KleiKodeshInstallerWpf\KleiKodesh_Main.ico"

Name "מתקין ${PRODUCT_NAME}"
OutFile "KleiKodeshSetup.exe"
InstallDir "$PROGRAMFILES\KleiKodesh"
RequestExecutionLevel admin
SilentInstall silent
AutoCloseWindow true

Function .onInit
  ; Check for .NET Framework
  Call CheckDotNetFramework
FunctionEnd

Function CheckDotNetFramework
  ; Check if .NET Framework 4.8 or higher is installed
  ReadRegDWORD $0 HKLM "${DOTNET_REGKEY}" "Release"
  ${If} $0 == ""
    MessageBox MB_OK|MB_ICONSTOP|MB_RTLREADING "נדרש .NET Framework ${DOTNET_VERSION} או גרסה חדשה יותר.$\r$\n$\r$\nאנא התקן את .NET Framework ונסה שוב.$\r$\n$\r$\nהורדה: https://dotnet.microsoft.com/download/dotnet-framework"
    Abort
  ${EndIf}
  
  ; Check version (Release value for .NET 4.8 is 528040 or higher)
  ${If} $0 < 528040
    MessageBox MB_OK|MB_ICONSTOP|MB_RTLREADING "נדרש .NET Framework ${DOTNET_VERSION} או גרסה חדשה יותר.$\r$\n$\r$\nהגרסה הנוכחית ישנה מהנדרש.$\r$\n$\r$\nאנא התקן את .NET Framework ${DOTNET_VERSION} ונסה שוב.$\r$\n$\r$\nהורדה: https://dotnet.microsoft.com/download/dotnet-framework"
    Abort
  ${EndIf}
FunctionEnd

Section "Main"
  ; Extract WPF installer to temp directory
  SetOutPath "$TEMP\KleiKodeshInstaller"
  
  ; Copy WPF installer files
  File "..\KleiKodeshInstallerWpf\bin\Release\net8.0-windows\KleiKodeshInstallerWpf.exe"
  File /nonfatal "..\KleiKodeshInstallerWpf\bin\Release\net8.0-windows\*.dll"
  File /nonfatal "..\KleiKodeshInstallerWpf\bin\Release\net8.0-windows\*.json"
  File "..\KleiKodeshInstallerWpf\KleiKodesh.zip"
  
  ; Check if silent mode was requested
  ${GetParameters} $R0
  StrCpy $R1 ""
  
  ; Check for --silent
  StrLen $R3 $R0
  ${If} $R3 > 0
    StrCpy $R2 $R0 8  ; Get first 8 chars
    ${If} $R2 == "--silent"
      StrCpy $R1 " --silent"
    ${Else}
      StrCpy $R2 $R0 7  ; Get first 7 chars  
      ${If} $R2 == "/silent"
        StrCpy $R1 " /silent"
      ${EndIf}
    ${EndIf}
  ${EndIf}
  
  ; Run WPF installer with or without silent argument
  ExecWait '"$TEMP\KleiKodeshInstaller\KleiKodeshInstallerWpf.exe"$R1' $0
  
  ; Clean up temp files
  RMDir /r "$TEMP\KleiKodeshInstaller"
  
  ${If} $0 == 0
    ; WPF installer succeeded, create uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"
    
    ; Registry entries for uninstaller
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\uninstall.exe"
    WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" "NoRepair" 1
  ${EndIf}
SectionEnd

Function un.onInit
  MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2|MB_RTLREADING "האם אתה בטוח שברצונך להסיר לחלוטין את ${PRODUCT_NAME} ואת כל הרכיבים שלו?" IDYES +2
  Abort
FunctionEnd

Section Uninstall
  ; Remove application files (WPF installer creates these)
  RMDir /r "$INSTDIR\Application"
  
  ; Remove installation directory
  RMDir /r "$INSTDIR"
  
  ; Remove registry keys created by WPF installer
  ; Office Add-in registry cleanup
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\Addins\KleiKodesh"
  DeleteRegKey HKCU "Software\Microsoft\Office\Excel\Addins\KleiKodesh"
  DeleteRegKey HKCU "Software\Microsoft\Office\PowerPoint\Addins\KleiKodesh"
  DeleteRegKey HKLM "Software\Microsoft\Office\Word\Addins\KleiKodesh"
  DeleteRegKey HKLM "Software\Microsoft\Office\Excel\Addins\KleiKodesh"
  DeleteRegKey HKLM "Software\Microsoft\Office\PowerPoint\Addins\KleiKodesh"
  
  ; VSTO registry cleanup
  DeleteRegKey HKCU "Software\Microsoft\VSTO\Security\Inclusion\*KleiKodesh*"
  DeleteRegKey HKLM "Software\Microsoft\VSTO\Security\Inclusion\*KleiKodesh*"
  
  ; Application-specific registry cleanup
  DeleteRegKey HKCU "Software\KleiKodesh"
  DeleteRegKey HKLM "Software\KleiKodesh"
  
  ; Remove uninstaller registry entries
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  
  ; Clean up Start Menu shortcuts (if any were created)
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\*.*"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
  
  ; Clean up Desktop shortcuts (if any were created)
  Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
  
  ; Close silently when completed
  SetAutoClose true
SectionEnd