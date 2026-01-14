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
echo 1. Full Release Build (AnyCPU) + GitHub Release
echo 2. Full Release Build (x64) + GitHub Release  
echo 3. Release Build Only (AnyCPU) - No GitHub Release
echo 4. Release Build Only (x64) - No GitHub Release
echo 5. Quick Build Test (No version increment)
echo 6. Clean Build (Delete old installers first)
echo 7. ClickOnce Build Menu (NSIS Auto-Extractor)
echo 8. View Build Logs
echo 9. Open Build Folder
echo 0. Exit
echo.
set /p choice="Enter your choice (0-9): "

if "%choice%"=="1" goto RELEASE_ANYCPU
if "%choice%"=="2" goto RELEASE_X64
if "%choice%"=="3" goto BUILD_ONLY_ANYCPU
if "%choice%"=="4" goto BUILD_ONLY_X64
if "%choice%"=="5" goto QUICK_BUILD
if "%choice%"=="6" goto CLEAN_BUILD
if "%choice%"=="7" goto CLICKONCE_MENU
if "%choice%"=="8" goto VIEW_LOGS
if "%choice%"=="9" goto OPEN_FOLDER
if "%choice%"=="0" goto EXIT

echo Invalid choice. Please try again.
pause
goto MAIN_MENU

:RELEASE_ANYCPU
cls
echo.
echo ===============================================
echo    Building Release (AnyCPU) + GitHub Release
echo ===============================================
echo.
echo This will:
echo - Build VSTO project in Release^|AnyCPU
echo - Build WPF installer
echo - Create NSIS wrapper
echo - Create GitHub release
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Platform AnyCPU
goto BUILD_COMPLETE

:RELEASE_X64
cls
echo.
echo ===============================================
echo     Building Release (x64) + GitHub Release
echo ===============================================
echo.
echo This will:
echo - Build VSTO project in Release^|x64
echo - Build WPF installer
echo - Create NSIS wrapper
echo - Create GitHub release
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Platform x64
goto BUILD_COMPLETE

:BUILD_ONLY_ANYCPU
cls
echo.
echo ===============================================
echo   Building Release (AnyCPU) - No GitHub Release
echo ===============================================
echo.
echo This will:
echo - Build VSTO project in Release^|AnyCPU
echo - Build WPF installer
echo - Create NSIS wrapper
echo - Skip GitHub release creation
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Platform AnyCPU -NoRelease
goto BUILD_COMPLETE

:BUILD_ONLY_X64
cls
echo.
echo ===============================================
echo    Building Release (x64) - No GitHub Release
echo ===============================================
echo.
echo This will:
echo - Build VSTO project in Release^|x64
echo - Build WPF installer
echo - Create NSIS wrapper
echo - Skip GitHub release creation
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Platform x64 -NoRelease
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
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Platform AnyCPU -NoRelease -NoWait
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
rmdir /s /q "%~dp0..\bin" 2>nul
rmdir /s /q "%~dp0..\obj" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVsto\bin" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVsto\obj" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVstoInstallerWpf\bin" 2>nul
rmdir /s /q "%~dp0..\KleiKodeshVstoInstallerWpf\obj" 2>nul

echo.
echo Starting clean build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" -Platform AnyCPU
goto BUILD_COMPLETE

:CLICKONCE_MENU
cls
echo.
echo ===============================================
echo         Launching ClickOnce Build Menu
echo ===============================================
echo.
echo Opening ClickOnce build system...
echo This will create NSIS auto-extractor installers
echo that extract ClickOnce files to AppData and run setup.
echo.
start "" "%~dp0ClickOnce\build-clickonce-menu.bat"
exit /b 0

:VIEW_LOGS
cls
echo.
echo ===============================================
echo              Build Information
echo ===============================================
echo.
echo Current installer files:
dir /b "%~dp0KleiKodeshSetup-*.exe" 2>nul
if errorlevel 1 echo No installer files found.

echo.
echo Recent build activity:
for /f "tokens=*" %%i in ('dir /b /o-d "%~dp0KleiKodeshSetup-*.exe" 2^>nul') do (
    echo Latest: %%i
    goto FOUND_LATEST
)
echo No recent builds found.

:FOUND_LATEST
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