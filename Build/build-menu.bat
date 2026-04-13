@echo off
setlocal enabledelayedexpansion
color 0A
title KleiKodesh Build Menu

:MAIN_MENU
cls
echo.
echo ===============================================
echo          KleiKodesh Build Menu
echo ===============================================
echo.
echo Select build option:
echo.
echo 1. Full Release Build + GitHub Release
echo 2. Release Build Only - No GitHub Release
echo 3. Quick Build Test (No version increment)
echo 4. Clean Build (Delete old installers first)
echo 5. View Build Logs
echo 6. Open Build Folder
echo 7. Clear All Releases
echo 0. Exit
echo.
set /p choice="Enter your choice (0-7): "

if "%choice%"=="1" goto RELEASE_BUILD
if "%choice%"=="2" goto BUILD_ONLY
if "%choice%"=="3" goto QUICK_BUILD
if "%choice%"=="4" goto CLEAN_BUILD
if "%choice%"=="5" goto VIEW_LOGS
if "%choice%"=="6" goto OPEN_FOLDER
if "%choice%"=="7" goto CLEAR_RELEASES
if "%choice%"=="0" goto EXIT

echo Invalid choice. Please try again.
pause
goto MAIN_MENU

:RELEASE_BUILD
cls
echo.
echo ===============================================
echo      Building Release (AnyCPU) + GitHub Release
echo ===============================================
echo.
echo Select version increment type:
echo 1. Patch (Bug fixes) - e.g., 1.2.3 -^> 1.2.4
echo 2. Minor (New features) - e.g., 1.2.3 -^> 1.3.0
echo 3. Major (Breaking changes) - e.g., 1.2.3 -^> 2.0.0
echo.
set /p verchoice="Enter version increment (1-3): "

if "%verchoice%"=="1" set INCREMENT=patch
if "%verchoice%"=="2" set INCREMENT=minor
if "%verchoice%"=="3" set INCREMENT=major

if not defined INCREMENT (
    echo Invalid choice. Returning to menu.
    pause
    goto MAIN_MENU
)

echo.
echo Select release notes source:
echo 1. Git commits only
echo 2. RELEASE_NOTES.txt file only
echo 3. Both (file + commits)
echo.
set /p noteschoice="Enter release notes source (1-3): "

if "%noteschoice%"=="1" set NOTESSOURCE=commits
if "%noteschoice%"=="2" set NOTESSOURCE=file
if "%noteschoice%"=="3" set NOTESSOURCE=both

if not defined NOTESSOURCE (
    echo Invalid choice. Returning to menu.
    pause
    goto MAIN_MENU
)

echo.
echo This will:
echo - Increment %INCREMENT% version
echo - Build VSTO project in Release^|AnyCPU
echo - Build WPF installer
echo - Create NSIS wrapper
echo - Create GitHub release with %NOTESSOURCE% notes
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -VersionIncrement %INCREMENT% -ReleaseNotesSource %NOTESSOURCE%
goto BUILD_COMPLETE

:BUILD_ONLY
cls
echo.
echo ===============================================
echo    Building Release (AnyCPU) - No GitHub Release
echo ===============================================
echo.
echo Select version increment type:
echo 1. Patch (Bug fixes) - e.g., 1.2.3 -^> 1.2.4
echo 2. Minor (New features) - e.g., 1.2.3 -^> 1.3.0
echo 3. Major (Breaking changes) - e.g., 1.2.3 -^> 2.0.0
echo.
set /p verchoice="Enter version increment (1-3): "

if "%verchoice%"=="1" set INCREMENT=patch
if "%verchoice%"=="2" set INCREMENT=minor
if "%verchoice%"=="3" set INCREMENT=major

if not defined INCREMENT (
    echo Invalid choice. Returning to menu.
    pause
    goto MAIN_MENU
)

echo.
echo This will:
echo - Increment %INCREMENT% version
echo - Build VSTO project in Release^|AnyCPU
echo - Build WPF installer
echo - Create NSIS wrapper
echo - Skip GitHub release creation
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -NoRelease -VersionIncrement %INCREMENT%
goto BUILD_COMPLETE

:QUICK_BUILD
cls
echo.
echo ===============================================
echo           Quick Build Test
echo ===============================================
echo.
echo This will:
echo - Build without version increment
echo - Use AnyCPU configuration
echo - Skip GitHub release
echo - Useful for testing build process
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting quick build...
echo Note: This is a test build - version will not be incremented
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -NoRelease -NoWait
goto BUILD_COMPLETE

:CLEAN_BUILD
cls
echo.
echo ===============================================
echo              Clean Build
echo ===============================================
echo.
echo This will:
echo - Delete all existing installer files
echo - Clean build output directories
echo - Perform fresh Release build (AnyCPU)
echo - Create GitHub release
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Cleaning old installers...
del /q "%~dp0KleiKodeshSetup-*.exe" 2>nul
echo Cleaning build directories...
rmdir /s /q "%~dp0..\KleiKodeshVsto\bin" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVsto\obj" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVstoInstallerWpf\bin" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVstoInstallerWpf\obj" 2>nul

echo.
echo Starting clean build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"
goto BUILD_COMPLETE

:VIEW_LOGS
cls
echo.
echo ===============================================
echo              Build Information
echo ===============================================
echo.
echo Current installer files:
dir /b "%~dp0releases\KleiKodeshSetup-*.exe" 2>nul
if errorlevel 1 echo No installer files found.

echo.
echo Build script location: %~dp0build-installer.ps1
echo.
pause
goto MAIN_MENU

:OPEN_FOLDER
cls
echo Opening Build folder...
explorer "%~dp0"
goto MAIN_MENU

:CLEAR_RELEASES
cls
echo.
echo ===============================================
echo      Clear All GitHub Releases
echo ===============================================
echo.
echo WARNING: This will permanently delete ALL releases from GitHub!
echo This action cannot be undone.
echo.
set /p confirm="Are you sure you want to delete ALL GitHub releases? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
set /p doubleconfirm="Type 'DELETE' to confirm: "
if /i not "%doubleconfirm%"=="DELETE" (
    echo Cancelled.
    pause
    goto MAIN_MENU
)

echo.
echo Deleting all GitHub releases...
powershell -ExecutionPolicy Bypass -Command "& { $releases = gh release list --limit 1000 --json tagName | ConvertFrom-Json; if ($releases.Count -eq 0) { Write-Host 'No releases found.'; exit 0 }; Write-Host \"Found $($releases.Count) releases. Deleting...\"; foreach ($release in $releases) { Write-Host \"Deleting release: $($release.tagName)\"; gh release delete $($release.tagName) --yes --cleanup-tag }; Write-Host 'All releases deleted successfully.' }"

echo.
echo ===============================================
echo    All GitHub Releases Cleared
echo ===============================================
echo.
pause
goto MAIN_MENU

:BUILD_COMPLETE
echo.
echo ===============================================
echo              Build Complete
echo ===============================================
echo.
echo Build process finished. Check output above for results.
echo.
echo Available options:
echo 1. Return to main menu
echo 2. Open Build folder
echo 3. Exit
echo.
set /p next="Enter your choice (1-3): "

if "%next%"=="1" goto MAIN_MENU
if "%next%"=="2" (
    explorer "%~dp0"
    goto MAIN_MENU
)
if "%next%"=="3" goto EXIT

goto MAIN_MENU

:EXIT
cls
echo.
echo Thank you for using KleiKodesh Build Menu!
echo.
pause
exit /b 0
