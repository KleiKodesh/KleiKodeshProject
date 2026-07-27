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
!ifndef PRODUCT_VERSION_NUMERIC
  !define PRODUCT_VERSION_NUMERIC "1.0.0"  ; Fallback (no "v" prefix, for VIProductVersion)
!endif
!define PRODUCT_PUBLISHER "צוות כלי קודש"

; Embed version info into the exe's PE header so FileVersionInfo can read it
; VIProductVersion requires exactly X.Y.Z.W (four numeric parts)
VIProductVersion "${PRODUCT_VERSION_NUMERIC}.0"
VIAddVersionKey "ProductName"     "${PRODUCT_NAME}"
VIAddVersionKey "ProductVersion"  "${PRODUCT_VERSION}"
VIAddVersionKey "FileVersion"     "${PRODUCT_VERSION_NUMERIC}.0"
VIAddVersionKey "FileDescription" "מתקין כלי קודש"
VIAddVersionKey "CompanyName"     "${PRODUCT_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "© צוות כלי קודש"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KleiKodesh"
!define PRODUCT_UNINST_ROOT_KEY "HKCU"
!define DOTNET_FRAMEWORK_VERSION "4.8"
!define VSTO_RUNTIME_VERSION "2010"

; MUI Settings
!define MUI_ABORTWARNING
!define MUI_UNICON "..\Installer\KleiKodesh_Main.ico"

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
LangString MSG_DOTNET_FRAMEWORK_REQUIRED ${LANG_HEBREW} "נדרש .NET Framework ${DOTNET_FRAMEWORK_VERSION} או גרסה חדשה יותר.$\r$\n$\r$\nרכיב זה נדרש להפעלת כלי קודש.$\r$\n$\r$\nהאם ברצונך לפתוח את דף ההורדה כעת?"
LangString MSG_VSTO_RUNTIME_REQUIRED ${LANG_HEBREW} "נדרש Microsoft Visual Studio ${VSTO_RUNTIME_VERSION} Tools for Office Runtime.$\r$\n$\r$\nרכיב זה נדרש להפעלת תוספי Office.$\r$\n$\r$\nהאם ברצונך להוריד ולהתקין אותו כעת?$\r$\n$\r$\nהורדה: https://www.microsoft.com/download/details.aspx?id=48217"
LangString MSG_PREREQUISITES_FAILED ${LANG_HEBREW} "התקנת הדרישות המוקדמות נכשלה.$\r$\n$\r$\nאנא התקן ידנית:$\r$\n• .NET Framework ${DOTNET_FRAMEWORK_VERSION}$\r$\n• VSTO Runtime ${VSTO_RUNTIME_VERSION}$\r$\n$\r$\nלאחר מכן נסה שוב."
LangString MSG_DOWNLOAD_VSTO ${LANG_HEBREW} "מוריד VSTO Runtime..."
LangString MSG_INSTALL_VSTO ${LANG_HEBREW} "מתקין VSTO Runtime..."
LangString MSG_WORD_RUNNING ${LANG_HEBREW} "Microsoft Word פועל כעת.$\r$\n$\r$\nהאם ברצונך לסגור את Word ולהמשיך בהסרה?"
LangString MSG_WORD_CLOSE_FAILED ${LANG_HEBREW} "לא ניתן לסגור את Word אוטומטית.$\r$\n$\r$\nאנא סגור את Word באופן ידני ונסה שוב."
LangString MSG_UNINSTALL_CONFIRM ${LANG_HEBREW} "האם אתה בטוח שברצונך להסיר לחלוטין את ${PRODUCT_NAME} ואת כל הרכיבים שלו?"

; Language strings for English (fallback)
LangString MSG_DOTNET_FRAMEWORK_REQUIRED ${LANG_ENGLISH} ".NET Framework ${DOTNET_FRAMEWORK_VERSION} or higher is required.$\r$\n$\r$\nThis component is needed to run KleiKodesh.$\r$\n$\r$\nWould you like to open the download page now?"
LangString MSG_VSTO_RUNTIME_REQUIRED ${LANG_ENGLISH} "Microsoft Visual Studio ${VSTO_RUNTIME_VERSION} Tools for Office Runtime is required.$\r$\n$\r$\nThis component is needed to run Office add-ins.$\r$\n$\r$\nWould you like to download and install it now?$\r$\n$\r$\nDownload: https://www.microsoft.com/download/details.aspx?id=48217"
LangString MSG_PREREQUISITES_FAILED ${LANG_ENGLISH} "Prerequisites installation failed.$\r$\n$\r$\nPlease install manually:$\r$\n• .NET Framework ${DOTNET_FRAMEWORK_VERSION}$\r$\n• VSTO Runtime ${VSTO_RUNTIME_VERSION}$\r$\n$\r$\nThen try again."
LangString MSG_DOWNLOAD_VSTO ${LANG_ENGLISH} "Downloading VSTO Runtime..."
LangString MSG_INSTALL_VSTO ${LANG_ENGLISH} "Installing VSTO Runtime..."
LangString MSG_WORD_RUNNING ${LANG_ENGLISH} "Microsoft Word is currently running.$\r$\n$\r$\nWould you like to close Word and continue with uninstallation?"
LangString MSG_WORD_CLOSE_FAILED ${LANG_ENGLISH} "Could not close Word automatically.$\r$\n$\r$\nPlease close Word manually and try again."
LangString MSG_UNINSTALL_CONFIRM ${LANG_ENGLISH} "Are you sure you want to completely remove ${PRODUCT_NAME} and all of its components?"

; Classic NSIS UI for minimal window
Icon "..\Installer\KleiKodesh_Main.ico"
UninstallIcon "..\Installer\KleiKodesh_Main.ico"

; Output directory - passed from build script, defaults to releases subfolder
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "releases"
!endif

; Filename suffix - passed from build script: "" | "-x64" | "-x86"
!ifndef OUTPUT_SUFFIX
  !define OUTPUT_SUFFIX ""
!endif

; WPF installer exe path - passed from build script to pick the correct variant output folder
!ifndef WPF_EXE_PATH
  !define WPF_EXE_PATH "..\Installer\bin\Release\net48\KleiKodeshVstoInstallerWpf.exe"
!endif

Name "מתקין ${PRODUCT_NAME}"
OutFile "${OUTPUT_DIR}\KleiKodeshSetup-${PRODUCT_VERSION}${OUTPUT_SUFFIX}.exe"
InstallDir "$LOCALAPPDATA\KleiKodesh"
; Per-user install (%LOCALAPPDATA% + HKCU) — MUST stay "user".
; "admin" here breaks the auto-updater: the update launch at Word/app close hits
; a surprise UAC prompt (declines are silent), and a standard user approving with
; admin credentials installs into the ADMIN's profile, so the real user never
; updates. The two places that genuinely need elevation prompt for it themselves:
;   - install: the WPF installer elevates DocumentLocator.Service.exe --install
;   - uninstall: the "sc stop/delete" pair below runs through one elevated cmd.exe
RequestExecutionLevel user
SilentInstall silent
AutoCloseWindow true

Function .onInit
  ; Detect system language and set appropriate language
  Call DetectSystemLanguage
  
  ; Check prerequisites in order
  Call CheckDotNetFramework48
  Call CheckVSTORuntime
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

Function CheckDotNetFramework48
  ; Check if .NET Framework 4.8 or higher is installed (required for both VSTO and installer)
  StrCpy $R0 "0"  ; Flag for .NET Framework 4.8+ found
  
  ; Method 1: Check registry for .NET Framework 4.8
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 >= 528040  ; .NET Framework 4.8 release number
    StrCpy $R0 "1"
    Goto framework_check_done
  ${EndIf}
  
  ; Method 2: Check WOW6432Node for 32-bit registry on 64-bit systems
  ReadRegDWORD $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 >= 528040  ; .NET Framework 4.8 release number
    StrCpy $R0 "1"
    Goto framework_check_done
  ${EndIf}
  
  ; If no .NET Framework 4.8+ found, offer to open download page
  ${If} $R0 == "0"
    ${If} $LANGUAGE == ${LANG_HEBREW}
      MessageBox MB_YESNO|MB_ICONEXCLAMATION|MB_RTLREADING "$(MSG_DOTNET_FRAMEWORK_REQUIRED)" IDYES OpenDotNetDownload IDNO AbortDotNet
    ${Else}
      MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(MSG_DOTNET_FRAMEWORK_REQUIRED)" IDYES OpenDotNetDownload IDNO AbortDotNet
    ${EndIf}

    OpenDotNetDownload:
      ExecShell "open" "https://dotnet.microsoft.com/download/dotnet-framework/net48"
      Quit

    AbortDotNet:
      Abort
  ${EndIf}

  framework_check_done:
FunctionEnd



Function CheckVSTORuntime
  ; Check if VSTO Runtime is installed
  StrCpy $R0 "0"  ; Flag for VSTO Runtime found
  
  ; Method 1: Check for VSTO 2010 Runtime (most common)
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\VSTO Runtime Setup\v4R" "Version"
  ${If} $0 != ""
    StrCpy $R0 "1"
    Goto vsto_check_done
  ${EndIf}
  
  ; Method 2: Check WOW6432Node for 32-bit registry on 64-bit systems
  ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\VSTO Runtime Setup\v4R" "Version"
  ${If} $0 != ""
    StrCpy $R0 "1"
    Goto vsto_check_done
  ${EndIf}
  
  ; Method 3: Check alternative registry locations
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\VSTO Runtime Setup\v4" "Version"
  ${If} $0 != ""
    StrCpy $R0 "1"
    Goto vsto_check_done
  ${EndIf}
  
  ; Method 4: Check for VSTO files in GAC
  ${If} ${FileExists} "$WINDIR\Microsoft.NET\assembly\GAC_MSIL\Microsoft.Office.Tools.v4.0.Framework"
    StrCpy $R0 "1"
    Goto vsto_check_done
  ${EndIf}
  
  ; If no VSTO Runtime found, offer to download and install
  ${If} $R0 == "0"
    ${If} $LANGUAGE == ${LANG_HEBREW}
      MessageBox MB_YESNO|MB_ICONQUESTION|MB_RTLREADING "$(MSG_VSTO_RUNTIME_REQUIRED)" IDYES DownloadVSTO IDNO AbortInstall
    ${Else}
      MessageBox MB_YESNO|MB_ICONQUESTION "$(MSG_VSTO_RUNTIME_REQUIRED)" IDYES DownloadVSTO IDNO AbortInstall
    ${EndIf}
    
    DownloadVSTO:
      Call DownloadAndInstallVSTO
      Goto vsto_check_done
      
    AbortInstall:
      Abort
  ${EndIf}
  
  vsto_check_done:
FunctionEnd

Function DownloadAndInstallVSTO
  ; Download and install VSTO Runtime
  StrCpy $R1 "$TEMP\vstor_redist.exe"
  
  ; Show download progress message
  ${If} $LANGUAGE == ${LANG_HEBREW}
    DetailPrint "$(MSG_DOWNLOAD_VSTO)"
  ${Else}
    DetailPrint "$(MSG_DOWNLOAD_VSTO)"
  ${EndIf}
  
  ; Try to download VSTO Runtime (using inetc plugin if available)
  ; Note: This requires the NSIS inetc plugin to be installed
  ; Alternative: Use NSISdl plugin or direct Windows API calls
  
  ; For now, we'll open the download page and ask user to install manually
  ${If} $LANGUAGE == ${LANG_HEBREW}
    MessageBox MB_OK|MB_ICONINFORMATION|MB_RTLREADING "אנא הורד והתקן את VSTO Runtime מהקישור שייפתח.$\r$\n$\r$\nלאחר ההתקנה, הפעל שוב את המתקין."
  ${Else}
    MessageBox MB_OK|MB_ICONINFORMATION "Please download and install VSTO Runtime from the link that will open.$\r$\n$\r$\nAfter installation, run this installer again."
  ${EndIf}
  
  ; Open download page
  ExecShell "open" "https://www.microsoft.com/download/details.aspx?id=48217"
  
  ; Exit installer so user can install VSTO Runtime first
  Quit
FunctionEnd

Section "Main"
  ; Extract WPF installer to temp directory
  SetOutPath "$TEMP\KleiKodeshInstaller"
  
  ; Copy WPF installer files (built for .NET Framework 4.8)
  File "${WPF_EXE_PATH}"
  File /nonfatal "${WPF_EXE_PATH}\..\*.config"
  File "..\Installer\KleiKodesh.zip"
  
  ; Pass all command-line arguments through to the WPF installer unchanged
  ${GetParameters} $R0
  
  ; Run WPF installer with all original arguments
  ExecWait '"$TEMP\KleiKodeshInstaller\KleiKodeshVstoInstallerWpf.exe" $R0' $0
  
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

  ; ── DocumentLocator Windows Service ──────────────────────────────────────────
  ; Must happen before file removal — the service exe must be on disk for
  ; sc.exe to stop and delete it. The uninstaller itself runs unelevated
  ; (RequestExecutionLevel user — see the note above), and SCM stop/delete needs
  ; admin, so just these two commands run through a single elevated cmd.exe
  ; (one UAC prompt). Skipped entirely when the service was never registered.
  ; If the user declines the prompt, the service is left behind (best effort)
  ; and the rest of the uninstall continues.
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Services\DocumentLocatorSvc" "ImagePath"
  StrCmp $0 "" DocLocatorSvcDone
  DetailPrint "עוצר ומסיר שירות DocumentLocator..."
  ; ping -n 4 ≈ 3s pause between stop and delete so the service reaches STOPPED
  ; and releases its exe lock before file removal below.
  ExecShellWait "runas" "$SYSDIR\cmd.exe" "/C sc stop DocumentLocatorSvc & ping -n 4 127.0.0.1 >nul & sc delete DocumentLocatorSvc" SW_HIDE
  ; Small grace period for the service process to fully exit.
  Sleep 500
  DocLocatorSvcDone:

  ; Show progress with hardcoded Hebrew messages
  DetailPrint "מתחיל הסרת כלי קודש..."

  ; ── Shell "Open With" / context-menu registry cleanup ────────────────────────
  DetailPrint "מנקה רישומי מעטפת (פתיחה עם)..."

  ; ProgId key (written by older versions — clean up on uninstall)
  DeleteRegKey HKCU "Software\Classes\KitveiHakodesh.Document.1"

  ; OpenWithProgids values (written by older versions — clean up on uninstall)
  DeleteRegValue HKCU "Software\Classes\.pdf\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Classes\.doc\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Classes\.docx\OpenWithProgids" "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Classes\.rtf\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Classes\.txt\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Classes\.htm\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Classes\.html\OpenWithProgids" "KitveiHakodesh.Document.1"

  ; Applications entry: HKCU\Software\Classes\Applications\כתבי הקודש.exe
  DeleteRegKey HKCU "Software\Classes\Applications\כתבי הקודש.exe"

  ; ── Default-handler ProgId keys (<ext>_auto_file) ────────────────────────────
  ; When the user sets כתבי הקודש as the *default* app for an extension via
  ; "Always use this app" or Windows Settings → Default Apps, Windows creates a
  ; per-user ProgId key named <ext>_auto_file under HKCU\Software\Classes and
  ; points the extension's UserChoice to it.  The app never writes these — Windows
  ; does — so they were never cleaned up before.
  ; Deleting the HKCU\Software\Classes key is sufficient: HKCR\<ext>_auto_file is
  ; only a merged view of HKCU\Software\Classes, so no elevation is required.
  ;
  ; SAFETY: before deleting, we read the shell\open\command value and verify it
  ; contains "KleiKodesh" — so we never touch a key that belongs to another app.
  !macro DeleteAutoFileIfOurs PROGID
    ReadRegStr $R0 HKCU "Software\Classes\${PROGID}\shell\open\command" ""
    Push $R0
    Push "KleiKodesh"
    Call un.StrStr
    Pop $R1
    StrCmp $R1 "" +2        ; skip delete if substring not found
      DeleteRegKey HKCU "Software\Classes\${PROGID}"
  !macroend
  !insertmacro DeleteAutoFileIfOurs "pdf_auto_file"
  !insertmacro DeleteAutoFileIfOurs "doc_auto_file"
  !insertmacro DeleteAutoFileIfOurs "docx_auto_file"
  !insertmacro DeleteAutoFileIfOurs "rtf_auto_file"
  !insertmacro DeleteAutoFileIfOurs "txt_auto_file"
  !insertmacro DeleteAutoFileIfOurs "htm_auto_file"
  !insertmacro DeleteAutoFileIfOurs "html_auto_file"

  ; ── Explorer FileExts cleanup ────────────────────────────────────────────────
  ; Windows writes its own copy of OpenWithProgids under FileExts when the user picks
  ; "Open With" from Explorer. These are separate from Software\Classes and must also
  ; be removed; otherwise Explorer still shows the uninstalled app in the context menu.
  ;
  ; We only delete the specific KitveiHakodesh.Document.1 value — other apps' entries
  ; in the same key are left untouched.
  ;
  ; We intentionally do NOT touch OpenWithList: that key is an MRU ordered list of all
  ; apps the user has opened this extension with, not just ours. Deleting the whole key
  ; would clear other apps' history. Since we remove the Applications\ key above,
  ; Windows cannot launch our exe from the menu regardless, and Explorer will stop
  ; showing it once SHChangeNotify fires.
  DetailPrint "מנקה רישומי FileExts (פתיחה עם)..."

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.doc\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.docx\OpenWithProgids" "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.rtf\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.txt\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.htm\OpenWithProgids"  "KitveiHakodesh.Document.1"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.html\OpenWithProgids" "KitveiHakodesh.Document.1"

  ; ── File system ──────────────────────────────────────────────────────────────
  DetailPrint "מסיר קבצי התוכנה..."
  ; Current install location (v1.0.24+)
  RMDir /r "$LOCALAPPDATA\KleiKodesh"
  ; Old install locations — English name (v1.0.x through ~v1.0.23)
  RMDir /r "$PROGRAMFILES\KleiKodesh"
  RMDir /r "$PROGRAMFILES32\KleiKodesh"
  ; Old install locations — Hebrew name (very first public release)
  RMDir /r "$PROGRAMFILES\כלי קודש"
  RMDir /r "$PROGRAMFILES32\כלי קודש"
  ; Old AppData\Roaming folder (old addin wrote RibbonSettings.csv here)
  RMDir /r "$APPDATA\KleiKodesh"
  ; Old standalone WebView2 cache (pre-v3.x)
  RMDir /r "$LOCALAPPDATA\WebView2SharedCache"
  ; Legacy WebView2 webcache — AppViewer.cs used %LocalAppData%\KitveiHakodesh\webcache
  ; before it was moved into the install folder. Entirely owned by KleiKodesh.
  RMDir /r "$LOCALAPPDATA\KitveiHakodesh"
  ; Split-name variant (defensive)
  RMDir /r "$LOCALAPPDATA\Klei Kodesh"

  ; ── Office Add-in registry — HKCU ────────────────────────────────────────────
  DetailPrint "מנקה רישומי Office..."
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\Addins\KleiKodesh"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\Addins\Klei Kodesh"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\Addins\כלי קודש"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\Addins\כליקודש"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\AddinsData\KleiKodesh"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\AddinsData\Klei Kodesh"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\AddinsData\כלי קודש"
  DeleteRegKey HKCU "Software\Microsoft\Office\Word\AddinsData\כליקודש"

  ; ── Office Add-in registry — HKLM (old versions, may need elevation) ─────────
  DeleteRegKey HKLM "Software\Microsoft\Office\Word\Addins\KleiKodesh"
  DeleteRegKey HKLM "Software\Microsoft\Office\Word\Addins\Klei Kodesh"
  DeleteRegKey HKLM "Software\Microsoft\Office\Word\Addins\כלי קודש"
  DeleteRegKey HKLM "Software\Microsoft\Office\Word\Addins\כליקודש"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Office\Word\Addins\KleiKodesh"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Office\Word\Addins\Klei Kodesh"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Office\Word\Addins\כלי קודש"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Office\Word\Addins\כליקודש"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Office\Word\AddinsData\KleiKodesh"

  ; ── Version stamp ─────────────────────────────────────────────────────────────
  DetailPrint "מנקה הגדרות תוכנה..."
  DeleteRegKey HKCU "SOFTWARE\KleiKodesh"

  ; ── Runtime settings (VB Program Settings) ───────────────────────────────────
  DeleteRegKey HKCU "Software\VB and VBA Program Settings\KleiKodesh"
  DeleteRegKey HKCU "Software\VB and VBA Program Settings\ZayitApp"
  ; KitveiHakodesh — written by the KitveiHakodesh app (HebrewBooks\CsvLastUpdated, etc.)
  DeleteRegKey HKCU "Software\VB and VBA Program Settings\KitveiHakodesh"

  ; ── Word per-addin metadata values ───────────────────────────────────────────
  ; Delete the KleiKodesh-named values from Word's per-addin tracking keys.
  ; These are individual values (not subkeys), named by the add-in's key name.
  !macro DeleteWordAddinValues OFFVER
    DeleteRegValue HKCU "Software\Microsoft\Office\${OFFVER}\Word\AddInLoadTimes" "KleiKodesh"
    DeleteRegValue HKCU "Software\Microsoft\Office\${OFFVER}\Word\AddinEventTimes\Connect" "KleiKodesh"
    DeleteRegValue HKCU "Software\Microsoft\Office\${OFFVER}\Word\AddinEventTimes\Shutdown" "KleiKodesh"
    DeleteRegValue HKCU "Software\Microsoft\Office\${OFFVER}\Word\NotifiedAddins" "KleiKodesh"
    DeleteRegValue HKCU "Software\Microsoft\Office\${OFFVER}\Common\CustomUIValidationCache" "KleiKodesh.Microsoft.Word.Document"
  !macroend
  !insertmacro DeleteWordAddinValues "12.0"
  !insertmacro DeleteWordAddinValues "14.0"
  !insertmacro DeleteWordAddinValues "15.0"
  !insertmacro DeleteWordAddinValues "16.0"

  ; ── VSTO Security entries (base64-keyed, requires enumeration) ────────────────
  DetailPrint "מנקה הגדרות אבטחה של VSTO..."
  Call un.CleanupVSTOSecurityEntries

  ; ── Uninstall entries — current version (HKCU) ───────────────────────────────
  DetailPrint "מסיר רישומי הסרה..."
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"

  ; ── Uninstall entries — old versions (HKLM, Hebrew key name) ─────────────────
  ; The old GitHub repo installer (v2.0.x) wrote to HKLM with key = "כלי קודש"
  ; DisplayName was "כלי קודש v{version}" — this is the second entry users see
  ; in Programs & Features. Removing it here eliminates the dual-app appearance.
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\כלי קודש"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\כלי קודש"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\כלי קודש"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Klei Kodesh"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Klei Kodesh"

  ; ── Shortcuts ─────────────────────────────────────────────────────────────────
  DetailPrint "מסיר קיצורי דרך..."
  Delete "$DESKTOP\כלי קודש.lnk"
  Delete "$DESKTOP\KleiKodesh.lnk"
  RMDir /r "$SMPROGRAMS\כלי קודש"
  RMDir /r "$SMPROGRAMS\KleiKodesh"

  DetailPrint "הסרת כלי קודש הושלמה בהצלחה!"

  ; Close silently when completed
  SetAutoClose true
SectionEnd

Function un.CleanupVSTOSecurityEntries
  ; Remove VSTO Security Inclusion entries
  ; These contain base64-encoded manifest URLs, so we enumerate and remove all KleiKodesh-related entries
  Push $0
  Push $1
  Push $2
  Push $3
  
  ; Clean up Inclusion list entries
  StrCpy $0 0
  EnumInclusionLoop:
    EnumRegKey $1 HKCU "SOFTWARE\Microsoft\VSTO\Security\Inclusion" $0
    StrCmp $1 "" InclusionDone
    
    ; Read the Url value to check if it's related to KleiKodesh
    ReadRegStr $2 HKCU "SOFTWARE\Microsoft\VSTO\Security\Inclusion\$1" "Url"
    
    ; Check if URL contains KleiKodesh using StrStr
    Push $2
    Push "KleiKodesh"
    Call un.StrStr
    Pop $3
    StrCmp $3 "" InclusionNext
    
    ; Delete this key if it contains KleiKodesh
    DeleteRegKey HKCU "SOFTWARE\Microsoft\VSTO\Security\Inclusion\$1"
    Goto EnumInclusionLoop  ; Don't increment counter since we deleted an entry
    
    InclusionNext:
      IntOp $0 $0 + 1
      Goto EnumInclusionLoop
  
  InclusionDone:
  
  ; Clean up TrustedPaths entries
  StrCpy $0 0
  EnumTrustedLoop:
    EnumRegKey $1 HKCU "SOFTWARE\Microsoft\VSTO\Security\TrustedPaths" $0
    StrCmp $1 "" TrustedDone
    
    ; Read the Path value to check if it's related to KleiKodesh
    ReadRegStr $2 HKCU "SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\$1" "Path"
    
    ; Check if Path contains KleiKodesh using StrStr
    Push $2
    Push "KleiKodesh"
    Call un.StrStr
    Pop $3
    StrCmp $3 "" TrustedNext
    
    ; Delete this key if it contains KleiKodesh
    DeleteRegKey HKCU "SOFTWARE\Microsoft\VSTO\Security\TrustedPaths\$1"
    Goto EnumTrustedLoop  ; Don't increment counter since we deleted an entry
    
    TrustedNext:
      IntOp $0 $0 + 1
      Goto EnumTrustedLoop
  
  TrustedDone:
  
  Pop $3
  Pop $2
  Pop $1
  Pop $0
FunctionEnd


; StrStr function for uninstaller - finds substring in string
; Input: Push string, Push substring
; Output: Pop result (empty if not found, remainder of string starting with substring if found)
Function un.StrStr
  Exch $R1 ; substring
  Exch
  Exch $R2 ; string
  Push $R3
  Push $R4
  Push $R5
  StrLen $R3 $R1
  StrCpy $R4 0
  loop:
    StrCpy $R5 $R2 $R3 $R4
    StrCmp $R5 $R1 done
    StrCmp $R5 "" done
    IntOp $R4 $R4 + 1
    Goto loop
  done:
    StrCpy $R1 $R5
    Pop $R5
    Pop $R4
    Pop $R3
    Pop $R2
    Exch $R1
FunctionEnd
