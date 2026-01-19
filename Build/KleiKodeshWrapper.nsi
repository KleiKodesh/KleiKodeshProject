; KleiKodesh Simple NSIS Wrapper
; Purpose: Check .NET dependencies, run WPF installer, and provide uninstaller

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!insertmacro GetParameters

!define PRODUCT_NAME "כלי קודש"
; Version is now passed as a parameter from build script
!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "v1.0.0"  ; Fallback version if not provided
!endif
!define PRODUCT_PUBLISHER "צוות כלי קודש"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"
!define DOTNET_VERSION "8.0"
!define DOTNET_REGKEY "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost"

; MUI Settings
!define MUI_ABORTWARNING
!define MUI_UNICON "..\KleiKodeshVstoInstallerWpf\KleiKodesh_Main.ico"

; Uninstaller window customization for Hebrew
!define MUI_UNINSTALLER
!define MUI_UNPAGE_INSTFILES_COLORS "0x000000 0xFFFFFF"

; Hebrew text for uninstaller window
!define MUI_UNINSTALLER_TITLE "הסרת כלי קודש"
!define MUI_UNINSTALLER_SUBTITLE "אנא המתן בזמן שכלי קודש מוסר מהמחשב שלך."

; Pages (required for MUI)
!insertmacro MUI_PAGE_INSTFILES

; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; Language support (must be after pages)
!insertmacro MUI_LANGUAGE "Hebrew"
!insertmacro MUI_LANGUAGE "English"

; Language strings for Hebrew
LangString MSG_DOTNET_REQUIRED ${LANG_HEBREW} "נדרש .NET ${DOTNET_VERSION} Desktop Runtime או גרסה חדשה יותר.$\r$\n$\r$\nאנא התקן את .NET Desktop Runtime ונסה שוב.$\r$\n$\r$\nהורדה: https://dotnet.microsoft.com/download/dotnet/${DOTNET_VERSION}"
LangString MSG_DOTNET_OLD_VERSION ${LANG_HEBREW} "נדרש .NET ${DOTNET_VERSION} Desktop Runtime או גרסה חדשה יותר.$\r$\n$\r$\nהגרסה הנוכחית ישנה מהנדרש.$\r$\n$\r$\nאנא התקן את .NET ${DOTNET_VERSION} Desktop Runtime ונסה שוב.$\r$\n$\r$\nהורדה: https://dotnet.microsoft.com/download/dotnet/${DOTNET_VERSION}"
LangString MSG_WORD_RUNNING ${LANG_HEBREW} "Microsoft Word פועל כעת.$\r$\n$\r$\nהאם ברצונך לסגור את Word ולהמשיך בהסרה?"
LangString MSG_WORD_CLOSE_FAILED ${LANG_HEBREW} "לא ניתן לסגור את Word אוטומטית.$\r$\n$\r$\nאנא סגור את Word באופן ידני ונסה שוב."
LangString MSG_UNINSTALL_CONFIRM ${LANG_HEBREW} "האם אתה בטוח שברצונך להסיר לחלוטין את ${PRODUCT_NAME} ואת כל הרכיבים שלו?"

; Language strings for English (fallback)
LangString MSG_DOTNET_REQUIRED ${LANG_ENGLISH} ".NET ${DOTNET_VERSION} Desktop Runtime or higher is required.$\r$\n$\r$\nPlease install .NET Desktop Runtime and try again.$\r$\n$\r$\nDownload: https://dotnet.microsoft.com/download/dotnet/${DOTNET_VERSION}"
LangString MSG_DOTNET_OLD_VERSION ${LANG_ENGLISH} ".NET ${DOTNET_VERSION} Desktop Runtime or higher is required.$\r$\n$\r$\nThe current version is older than required.$\r$\n$\r$\nPlease install .NET ${DOTNET_VERSION} Desktop Runtime and try again.$\r$\n$\r$\nDownload: https://dotnet.microsoft.com/download/dotnet/${DOTNET_VERSION}"
LangString MSG_WORD_RUNNING ${LANG_ENGLISH} "Microsoft Word is currently running.$\r$\n$\r$\nWould you like to close Word and continue with uninstallation?"
LangString MSG_WORD_CLOSE_FAILED ${LANG_ENGLISH} "Could not close Word automatically.$\r$\n$\r$\nPlease close Word manually and try again."
LangString MSG_UNINSTALL_CONFIRM ${LANG_ENGLISH} "Are you sure you want to completely remove ${PRODUCT_NAME} and all of its components?"

; Classic NSIS UI for minimal window
Icon "..\KleiKodeshVstoInstallerWpf\KleiKodesh_Main.ico"
UninstallIcon "..\KleiKodeshVstoInstallerWpf\KleiKodesh_Main.ico"

Name "מתקין ${PRODUCT_NAME}"
OutFile "KleiKodeshSetup-${PRODUCT_VERSION}.exe"
InstallDir "$LOCALAPPDATA\KleiKodesh"
RequestExecutionLevel admin
SilentInstall silent
AutoCloseWindow true

Function .onInit
  ; Detect system language and set appropriate language
  Call DetectSystemLanguage
  
  ; Check for .NET Framework
  Call CheckDotNetFramework
FunctionEnd

Function DetectSystemLanguage
  ; Get system default language ID
  System::Call 'kernel32::GetSystemDefaultLangID() i .r0'
  
  ; Hebrew language ID is 1037 (0x040D)
  ${If} $0 == 1037
    ; System is Hebrew, use Hebrew language
    StrCpy $LANGUAGE ${LANG_HEBREW}
  ${Else}
    ; System is not Hebrew, use English as fallback
    StrCpy $LANGUAGE ${LANG_ENGLISH}
  ${EndIf}
FunctionEnd

Function CheckDotNetFramework
  ; Check if .NET 8.0 Desktop Runtime is installed
  ; Method 1: Check for dotnet.exe and query runtimes
  StrCpy $R0 "0"  ; Flag for .NET 8 Desktop Runtime found
  
  ${If} ${FileExists} "$PROGRAMFILES\dotnet\dotnet.exe"
    ; Try to list runtimes and check for Microsoft.WindowsDesktop.App 8.x
    nsExec::ExecToStack '"$PROGRAMFILES\dotnet\dotnet.exe" --list-runtimes'
    Pop $0 ; Exit code
    Pop $1 ; Output
    ${If} $0 == 0
      ; Simple check: if output contains "Microsoft.WindowsDesktop.App 8."
      ; We'll use a simple string search approach
      StrLen $2 $1
      ${If} $2 > 30  ; Output should be substantial if runtimes are listed
        ; Look for the pattern in the output
        StrCpy $3 0
        ${Do}
          StrCpy $4 $1 30 $3  ; Get 30 chars starting at position $3
          StrCmp $4 "" done  ; End of string
          StrCpy $5 $4 26  ; Get first 26 chars: "Microsoft.WindowsDesktop.App 8"
          StrCmp $5 "Microsoft.WindowsDesktop.A" 0 +3
            StrCpy $6 $4 28  ; Get 28 chars to include version
            StrCpy $7 $6 27  ; "Microsoft.WindowsDesktop.App 8"
            StrCmp $7 "Microsoft.WindowsDesktop.App 8" found_runtime
          IntOp $3 $3 + 1
        ${Loop}
        done:
      ${EndIf}
    ${EndIf}
  ${EndIf}
  
  ; Method 2: Try 32-bit dotnet if 64-bit not found
  ${If} $R0 == "0"
    ${If} ${FileExists} "$PROGRAMFILES32\dotnet\dotnet.exe"
      nsExec::ExecToStack '"$PROGRAMFILES32\dotnet\dotnet.exe" --list-runtimes'
      Pop $0 ; Exit code
      Pop $1 ; Output
      ${If} $0 == 0
        StrLen $2 $1
        ${If} $2 > 30
          StrCpy $3 0
          ${Do}
            StrCpy $4 $1 30 $3
            StrCmp $4 "" done2
            StrCpy $5 $4 26
            StrCmp $5 "Microsoft.WindowsDesktop.A" 0 +3
              StrCpy $6 $4 28
              StrCpy $7 $6 27
              StrCmp $7 "Microsoft.WindowsDesktop.App 8" found_runtime
            IntOp $3 $3 + 1
          ${Loop}
          done2:
        ${EndIf}
      ${EndIf}
    ${EndIf}
  ${EndIf}
  
  ; Method 3: Check registry as fallback
  ${If} $R0 == "0"
    ; Check for .NET 8 in registry
    ReadRegStr $0 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\Microsoft.WindowsDesktop.App" ""
    ${If} $0 != ""
      ; Check if any 8.x version exists
      EnumRegValue $1 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\Microsoft.WindowsDesktop.App" 0
      ${If} $1 != ""
        StrCpy $2 $1 1  ; Get first character
        ${If} $2 == "8"
          StrCpy $R0 "1"
        ${EndIf}
      ${EndIf}
    ${EndIf}
  ${EndIf}
  
  ; If no .NET 8 Desktop Runtime found, show error
  ${If} $R0 == "0"
    ${If} $LANGUAGE == ${LANG_HEBREW}
      MessageBox MB_OK|MB_ICONSTOP|MB_RTLREADING "$(MSG_DOTNET_REQUIRED)"
    ${Else}
      MessageBox MB_OK|MB_ICONSTOP "$(MSG_DOTNET_REQUIRED)"
    ${EndIf}
    Abort
  ${EndIf}
  
  Goto end_check
  
  found_runtime:
    StrCpy $R0 "1"
    Goto end_check
  
  end_check:
FunctionEnd

Section "Main"
  ; Extract WPF installer to temp directory
  SetOutPath "$TEMP\KleiKodeshInstaller"
  
  ; Copy WPF installer files (built for .NET 8.0)
  File "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\KleiKodeshVstoInstallerWpf.exe"
  File /nonfatal "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\*.dll"
  File /nonfatal "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\*.json"
  File /nonfatal "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\*.runtimeconfig.json"
  File "..\KleiKodeshVstoInstallerWpf\KleiKodesh.zip"
  
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
  ExecWait '"$TEMP\KleiKodeshInstaller\KleiKodeshVstoInstallerWpf.exe"$R1' $0
  
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
  ; MUI_UNPAGE_CONFIRM will show the confirmation page
  ; No need for manual MessageBox here
FunctionEnd

Function un.HandleWordRunning
  ; Check if Word is running and handle it
  Call un.CheckWordRunning
  ${If} $R9 == "1"
    ; Word is running, ask user if they want to close it with hardcoded Hebrew message
    MessageBox MB_ICONQUESTION|MB_YESNO|MB_RTLREADING "Microsoft Word פועל כעת.$\r$\n$\r$\nהאם ברצונך לסגור את Word ולהמשיך בהסרה?" IDYES CloseWord IDNO AbortUninstall
    
    CloseWord:
      ; Try to close Word gracefully
      DetailPrint "סוגר את Microsoft Word..."
      Call un.CloseWord
      
      ; Wait briefly for Word to close
      Sleep 500
      
      ; Check again if Word is still running
      Call un.CheckWordRunning
      ${If} $R9 == "1"
        ; Word is still running, show error and abort with hardcoded Hebrew message
        MessageBox MB_OK|MB_ICONSTOP|MB_RTLREADING "לא ניתן לסגור את Word אוטומטית.$\r$\n$\r$\nאנא סגור את Word באופן ידני ונסה שוב."
        Abort
      ${EndIf}
      DetailPrint "Microsoft Word נסגר בהצלחה."
      Goto WordHandled
    
    AbortUninstall:
      Abort
    
    WordHandled:
  ${EndIf}
FunctionEnd

Function un.CloseWord
  ; Try to close Word gracefully first
  nsExec::ExecToStack 'taskkill /IM "WINWORD.EXE" /T'
  Pop $0 ; Exit code
  Pop $1 ; Output (not used)
  
  ; Wait briefly for graceful close
  Sleep 500
  
  ; Check if Word is still running
  Call un.CheckWordRunning
  ${If} $R9 == "1"
    ; Graceful close failed, try force close
    nsExec::ExecToStack 'taskkill /IM "WINWORD.EXE" /F /T'
    Pop $0 ; Exit code
    Pop $1 ; Output (not used)
    
    ; Wait briefly for force close
    Sleep 300
  ${EndIf}
FunctionEnd


Function un.CheckWordRunning
  ; Use faster process check with findstr for immediate response
  nsExec::ExecToStack 'cmd /c "tasklist /NH /FI "IMAGENAME eq WINWORD.EXE" | findstr /I WINWORD.EXE"'
  Pop $0 ; Exit code
  Pop $1 ; Output (not used)
  
  ; Exit code 0 means Word was found, 1 means not found
  ${If} $0 == 0
    StrCpy $R9 "1"  ; Word is running
  ${Else}
    StrCpy $R9 "0"  ; Word is not running
  ${EndIf}
FunctionEnd

Section Uninstall
  ; Check if Word is running after user confirms uninstall
  Call un.HandleWordRunning
  
  ; Show progress with hardcoded Hebrew messages
  DetailPrint "מתחיל הסרת כלי קודש..."
  
  ; Remove exact files and directories created by WPF installer
  ; WPF installer extracts to $LOCALAPPDATA\KleiKodesh
  DetailPrint "מסיר קבצי התוכנה..."
  RMDir /r "$LOCALAPPDATA\KleiKodesh"
  
  ; Remove exact registry entries created by WPF installer
  ; Office Add-in registry cleanup (Word only, current user registry)
  ; The WPF installer creates these in HKCU
  DetailPrint "מנקה רישומי רישום של Office..."
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\Addins\KleiKodesh"
  
  ; Version registry cleanup (created by SaveVersionToRegistry in HKCU)
  DetailPrint "מנקה הגדרות תוכנה..."
  DeleteRegKey HKCU "SOFTWARE\KleiKodesh"
  
  ; Remove uninstaller registry entries (created by NSIS)
  DetailPrint "מסיר רישומי הסרה..."
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  
  DetailPrint "הסרת כלי קודש הושלמה בהצלחה!"
  
  ; Close silently when completed
  SetAutoClose true
SectionEnd