@echo off
setlocal

REM Get the directory where this script is located
set "SCRIPT_DIR=%~dp0"

REM Change to the script directory (zayit-vue)
cd /d "%SCRIPT_DIR%"

echo ========================================
echo Building Vue Application for Zayit
echo ========================================
echo.
echo Working directory: %CD%
echo.

echo [1/3] Building production bundle...
call npm run build
if errorlevel 1 (
    echo ERROR: Build failed!
    exit /b 1
)
echo Build complete!
echo.

echo [2/3] Copying to C# project...
set "DEST_HTML=..\Zayit-cs\Zayit\Html\index.html"
copy /Y dist\index.html "%DEST_HTML%"
if errorlevel 1 (
    echo ERROR: HTML copy failed! Check if destination path exists.
    exit /b 1
)
echo Copy complete!
echo.

echo [3/3] Verifying deployment...
if exist "%DEST_HTML%" (
    echo SUCCESS: HTML deployed to %DEST_HTML%
) else (
    echo ERROR: HTML file not found at destination!
    exit /b 1
)
echo.

echo ========================================
echo Build and Deploy Complete!
echo ========================================
endlocal
