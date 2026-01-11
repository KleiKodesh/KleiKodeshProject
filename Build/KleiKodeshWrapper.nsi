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
!define DOTNET_VERSION "4.8"
!define DOTNET_REGKEY "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"

; MUI Settings
!define MUI_ABORTWARNING

; Uninstaller window customization for Hebrew
!define MUI_UNINSTALLER
!define MUI_UNPAGE_INSTFILES_COLORS "0x000000 0xFFFFFF"

; Hebrew text for uninstaller window
!define MUI_UNINSTALLER_TITLE "הסרת כלי קודש"
!define MUI_UNINSTALLER_SUBTITLE "אנא המתן בזמן שכלי קודש מוסר מהמחשב שלך."

; Pages (required for MUI)
!insertmacro MUI_PAGE_INSTFILES

; Uninstaller pages
!insertmacro MUI_UNPAGE_INSTFILES

; Language support (must be after pages)
!insertmacro MUI_LANGUAGE "Hebrew"
!insertmacro MUI_LANGUAGE "English"

; Language strings for Hebrew
LangString MSG_DOTNET_REQUIRED ${LANG_HEBREW} "נדרש .NET Framework ${DOTNET_VERSION} או גרסה חדשה יותר.$\r$\n$\r$\nאנא התקן את .NET Framework ונסה שוב.$\r$\n$\r$\nהורדה: https://dotnet.microsoft.com/download/dotnet-framework"
LangString MSG_DOTNET_OLD_VERSION ${LANG_HEBREW} "נדרש .NET Framework ${DOTNET_VERSION} או גרסה חדשה יותר.$\r$\n$\r$\nהגרסה הנוכחית ישנה מהנדרש.$\r$\n$\r$\nאנא התקן את .NET Framework ${DOTNET_VERSION} ונסה שוב.$\r$\n$\r$\nהורדה: https://dotnet.microsoft.com/download/dotnet-framework"
LangString MSG_WORD_RUNNING ${LANG_HEBREW} "Microsoft Word פועל כעת.$\r$\n$\r$\nהאם ברצונך לסגור את Word ולהמשיך בהסרה?"
LangString MSG_WORD_CLOSE_FAILED ${LANG_HEBREW} "לא ניתן לסגור את Word אוטומטית.$\r$\n$\r$\nאנא סגור את Word באופן ידני ונסה שוב."
LangString MSG_UNINSTALL_CONFIRM ${LANG_HEBREW} "האם אתה בטוח שברצונך להסיר לחלוטין את ${PRODUCT_NAME} ואת כל הרכיבים שלו?"

; Language strings for English (fallback)
LangString MSG_DOTNET_REQUIRED ${LANG_ENGLISH} ".NET Framework ${DOTNET_VERSION} or higher is required.$\r$\n$\r$\nPlease install .NET Framework and try again.$\r$\n$\r$\nDownload: https://dotnet.microsoft.com/download/dotnet-framework"
LangString MSG_DOTNET_OLD_VERSION ${LANG_ENGLISH} ".NET Framework ${DOTNET_VERSION} or higher is required.$\r$\n$\r$\nThe current version is older than required.$\r$\n$\r$\nPlease install .NET Framework ${DOTNET_VERSION} and try again.$\r$\n$\r$\nDownload: https://dotnet.microsoft.com/download/dotnet-framework"
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
  ; Check if .NET Framework 4.8 or higher is installed
  ReadRegDWORD $0 HKLM "${DOTNET_REGKEY}" "Release"
  ${If} $0 == ""
    ; Use appropriate RTL reading for Hebrew, normal for English
    ${If} $LANGUAGE == ${LANG_HEBREW}
      MessageBox MB_OK|MB_ICONSTOP|MB_RTLREADING "$(MSG_DOTNET_REQUIRED)"
    ${Else}
      MessageBox MB_OK|MB_ICONSTOP "$(MSG_DOTNET_REQUIRED)"
    ${EndIf}
    Abort
  ${EndIf}
  
  ; Check version (Release value for .NET 4.8 is 528040 or higher)
  ${If} $0 < 528040
    ${If} $LANGUAGE == ${LANG_HEBREW}
      MessageBox MB_OK|MB_ICONSTOP|MB_RTLREADING "$(MSG_DOTNET_OLD_VERSION)"
    ${Else}
      MessageBox MB_OK|MB_ICONSTOP "$(MSG_DOTNET_OLD_VERSION)"
    ${EndIf}
    Abort
  ${EndIf}
FunctionEnd

Section "Main"
  ; Extract WPF installer to temp directory
  SetOutPath "$TEMP\KleiKodeshInstaller"
  
  ; Copy WPF installer files
  File "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\KleiKodeshVstoInstallerWpf.exe"
  File /nonfatal "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\*.dll"
  File /nonfatal "..\KleiKodeshVstoInstallerWpf\bin\Release\net8.0-windows\*.json"
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
  ; Check if Word is running and offer to close it
  Call un.HandleWordRunning
  
  ; Skip confirmation dialog - proceed directly to uninstall
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
      
      ; Wait a moment for Word to close
      Sleep 2000
      
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
  
  ; Wait a moment for graceful close
  Sleep 2000
  
  ; Check if Word is still running
  Call un.CheckWordRunning
  ${If} $R9 == "1"
    ; Graceful close failed, try force close
    nsExec::ExecToStack 'taskkill /IM "WINWORD.EXE" /F /T'
    Pop $0 ; Exit code
    Pop $1 ; Output (not used)
    
    ; Wait a moment for force close
    Sleep 1000
  ${EndIf}
FunctionEnd


Function un.CheckWordRunning
  ; Check if Word is running using a more reliable method
  nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq WINWORD.EXE" /NH'
  Pop $0 ; Exit code
  Pop $1 ; Output
  
  ; If exit code is 0, check the output content
  ${If} $0 == 0
    ; Check if output starts with "INFO:" which means no processes found
    StrCpy $2 $1 5  ; Get first 5 characters
    ${If} $2 == "INFO:"
      StrCpy $R9 "0"  ; Word is not running
    ${Else}
      ; Check if output is empty or very short (also means no processes)
      StrLen $3 $1
      ${If} $3 < 10
        StrCpy $R9 "0"  ; Word is not running
      ${Else}
        StrCpy $R9 "1"  ; Word is running
      ${EndIf}
    ${EndIf}
  ${Else}
    ; If tasklist failed, assume Word is not running
    StrCpy $R9 "0"
  ${EndIf}
FunctionEnd

Section Uninstall
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