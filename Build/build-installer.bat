@echo off
echo Building KleiKodesh Installer...

REM Build WPF installer first
echo Building WPF installer in Release mode...
dotnet build "..\KleiKodeshInstallerWpf\KleiKodeshInstallerWpf.csproj" -c Release

if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build WPF installer
    pause
    exit /b 1
)

REM Check for NSIS
set "NSIS_PATH=C:\Program Files (x86)\NSIS\makensis.exe"
if not exist "%NSIS_PATH%" (
    set "NSIS_PATH=C:\Program Files\NSIS\makensis.exe"
    if not exist "%NSIS_PATH%" (
        echo ERROR: NSIS not found
        echo Install from: https://nsis.sourceforge.io/
        pause
        exit /b 1
    )
)

REM Build NSIS wrapper
echo Building NSIS wrapper...
"%NSIS_PATH%" KleiKodeshWrapper.nsi

if %ERRORLEVEL% equ 0 (
    echo.
    echo SUCCESS: KleiKodeshSetup.exe created!
    echo This wrapper checks .NET and runs your WPF installer.
) else (
    echo ERROR: NSIS build failed
    pause
    exit /b 1
)

pause