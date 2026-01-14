@echo off
setlocal enabledelayedexpansion
color 0B
title KleiKodesh ClickOnce Build Menu

:MAIN_MENU
cls
echo.
echo ===============================================
echo       KleiKodesh ClickOnce Build Menu
echo ===============================================
echo.
echo NOTE: ClickOnce builds require Visual Studio with Office workload
echo If you don't have Visual Studio, use the main WPF installer instead.
echo.
echo Select ClickOnce build option:
echo.
echo 1. Build ClickOnce (AnyCPU) + NSIS Auto-Extractor
echo 2. Build ClickOnce (x64) + NSIS Auto-Extractor
echo 3. Build ClickOnce Only (AnyCPU) - No NSIS wrapper
echo 4. Build ClickOnce Only (x64) - No NSIS wrapper
echo 5. Quick Test Build (No version increment)
echo 6. Clean ClickOnce Output
echo 7. View ClickOnce Files
echo 8. Test ClickOnce Installation
echo 9. Open ClickOnce Folder
echo A. Return to Main Build Menu
echo 0. Exit
echo.
set /p choice="Enter your choice (0-9, A): "

if "%choice%"=="1" goto CLICKONCE_ANYCPU
if "%choice%"=="2" goto CLICKONCE_X64
if "%choice%"=="3" goto CLICKONCE_ONLY_ANYCPU
if "%choice%"=="4" goto CLICKONCE_ONLY_X64
if "%choice%"=="5" goto QUICK_TEST
if "%choice%"=="6" goto CLEAN_CLICKONCE
if "%choice%"=="7" goto VIEW_FILES
if "%choice%"=="8" goto TEST_INSTALL
if "%choice%"=="9" goto OPEN_FOLDER
if /i "%choice%"=="A" goto MAIN_BUILD_MENU
if "%choice%"=="0" goto EXIT

echo Invalid choice. Please try again.
pause
goto MAIN_MENU

:CLICKONCE_ANYCPU
cls
echo.
echo ===============================================
echo   Building ClickOnce (AnyCPU) + NSIS Wrapper
echo ===============================================
echo.
echo This will:
echo - Build VSTO project for ClickOnce deployment
echo - Create ClickOnce manifests and setup.exe
echo - Package everything in NSIS auto-extractor
echo - Extract to %%APPDATA%%\KleiKodesh\ClickOnceTemp
echo - Run ClickOnce setup automatically
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting ClickOnce build with NSIS wrapper...
powershell -ExecutionPolicy Bypass -File "%~dp0build-clickonce.ps1" -Platform AnyCPU -NoWait
goto BUILD_COMPLETE

:CLICKONCE_X64
cls
echo.
echo ===============================================
echo    Building ClickOnce (x64) + NSIS Wrapper
echo ===============================================
echo.
echo WARNING: x64 build may fail due to project dependencies.
echo AnyCPU is recommended for better compatibility.
echo.
echo This will:
echo - Build VSTO project for ClickOnce deployment (x64)
echo - Create ClickOnce manifests and setup.exe
echo - Package everything in NSIS auto-extractor
echo - Extract to %%APPDATA%%\KleiKodesh\ClickOnceTemp
echo - Run ClickOnce setup automatically
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting ClickOnce build with NSIS wrapper...
powershell -ExecutionPolicy Bypass -File "%~dp0build-clickonce.ps1" -Platform x64 -NoWait
goto BUILD_COMPLETE

:CLICKONCE_ONLY_ANYCPU
cls
echo.
echo ===============================================
echo     ClickOnce Only Build (AnyCPU)
echo ===============================================
echo.
echo This will:
echo - Build VSTO project for ClickOnce deployment
echo - Create ClickOnce manifests and setup.exe
echo - Skip NSIS wrapper creation
echo - Output raw ClickOnce files only
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Building ClickOnce deployment only...
echo Note: This will stop after ClickOnce creation, before NSIS packaging
pause
echo You'll need to manually stop the script after ClickOnce build completes.
powershell -ExecutionPolicy Bypass -File "%~dp0build-clickonce.ps1" -Platform AnyCPU -NoWait
goto BUILD_COMPLETE

:CLICKONCE_ONLY_X64
cls
echo.
echo ===============================================
echo      ClickOnce Only Build (x64)
echo ===============================================
echo.
echo WARNING: x64 build may fail due to project dependencies.
echo AnyCPU is recommended for better compatibility.
echo.
echo This will:
echo - Build VSTO project for ClickOnce deployment (x64)
echo - Create ClickOnce manifests and setup.exe
echo - Skip NSIS wrapper creation
echo - Output raw ClickOnce files only
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Building ClickOnce deployment only...
echo Note: This will stop after ClickOnce creation, before NSIS packaging
pause
echo You'll need to manually stop the script after ClickOnce build completes.
powershell -ExecutionPolicy Bypass -File "%~dp0build-clickonce.ps1" -Platform x64 -NoWait
goto BUILD_COMPLETE

:QUICK_TEST
cls
echo.
echo ===============================================
echo        Quick Test Build (No Version Increment)
echo ===============================================
echo.
echo This will:
echo - Build ClickOnce deployment (AnyCPU)
echo - Use current version (no increment)
echo - Create NSIS auto-extractor
echo - Useful for testing without affecting version numbers
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Starting quick test build...
powershell -ExecutionPolicy Bypass -File "%~dp0build-clickonce.ps1" -Platform AnyCPU -NoVersionIncrement -NoWait
goto BUILD_COMPLETE

:CLEAN_CLICKONCE
cls
echo.
echo ===============================================
echo         Clean ClickOnce Output
echo ===============================================
echo.
echo This will delete:
echo - All ClickOnce output files
echo - Generated NSIS scripts
echo - Temporary extraction directories
echo - Previous installer files
echo.
set /p confirm="Continue? (Y/N): "
if /i not "%confirm%"=="Y" goto MAIN_MENU

echo.
echo Cleaning ClickOnce output...
rmdir /s /q "%~dp0ClickOnceOutput" 2>nul
rmdir /s /q "%~dp0TempExtraction" 2>nul
del /q "%~dp0KleiKodeshClickOnce*.exe" 2>nul
del /q "%~dp0KleiKodeshClickOnce.nsi" 2>nul
echo Cleanup complete!
pause
goto MAIN_MENU

:VIEW_FILES
cls
echo.
echo ===============================================
echo           ClickOnce Build Files
echo ===============================================
echo.
echo Current ClickOnce installers:
dir /b "%~dp0KleiKodeshClickOnce-*.exe" 2>nul
if errorlevel 1 echo No ClickOnce installers found.

echo.
echo ClickOnce output directory contents:
if exist "%~dp0ClickOnceOutput" (
    dir "%~dp0ClickOnceOutput" /s
) else (
    echo ClickOnceOutput directory not found.
)

echo.
echo Generated NSIS scripts:
dir /b "%~dp0*.nsi" 2>nul
if errorlevel 1 echo No NSIS scripts found.

echo.
pause
goto MAIN_MENU

:TEST_INSTALL
cls
echo.
echo ===============================================
echo        Test ClickOnce Installation
echo ===============================================
echo.
echo Available installers:
set count=0
for %%f in ("%~dp0KleiKodeshClickOnce-*.exe") do (
    set /a count+=1
    echo !count!. %%~nxf
    set "installer!count!=%%f"
)

if %count%==0 (
    echo No installers found. Build one first.
    pause
    goto MAIN_MENU
)

echo.
set /p testChoice="Select installer to test (1-%count%) or 0 to cancel: "

if "%testChoice%"=="0" goto MAIN_MENU
if %testChoice% gtr %count% (
    echo Invalid choice.
    pause
    goto MAIN_MENU
)

set selectedInstaller=!installer%testChoice%!
echo.
echo Testing installer: %selectedInstaller%
echo.
echo WARNING: This will actually install the add-in!
set /p confirmTest="Continue with test installation? (Y/N): "
if /i not "%confirmTest%"=="Y" goto MAIN_MENU

echo.
echo Running installer...
"%selectedInstaller%"
echo.
echo Test installation completed. Check Word for the add-in.
pause
goto MAIN_MENU

:OPEN_FOLDER
cls
echo Opening ClickOnce build folder...
explorer "%~dp0"
goto MAIN_MENU

:MAIN_BUILD_MENU
cls
echo Returning to main build menu...
start "" "%~dp0..\build-menu.bat"
exit /b 0

:BUILD_COMPLETE
echo.
echo ===============================================
echo           ClickOnce Build Complete
echo ===============================================
echo.
echo Build process finished. Check output above for results.
echo.
echo Available options:
echo 1. Return to ClickOnce menu
echo 2. Test the installer
echo 3. Open build folder
echo 4. Return to main build menu
echo 5. Exit
echo.
set /p next="Enter your choice (1-5): "

if "%next%"=="1" goto MAIN_MENU
if "%next%"=="2" goto TEST_INSTALL
if "%next%"=="3" (
    explorer "%~dp0"
    goto MAIN_MENU
)
if "%next%"=="4" goto MAIN_BUILD_MENU
if "%next%"=="5" goto EXIT

goto MAIN_MENU

:EXIT
cls
echo.
echo Thank you for using KleiKodesh ClickOnce Build Menu!
echo.
pause
exit /b 0