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

echo [1/4] Building production bundle...
call npm run build
if errorlevel 1 (
    echo ERROR: Build failed!
    exit /b 1
)
echo Build complete!
echo.

echo [2/4] Copying HTML to C# project...
set "DEST_HTML=..\Zayit-cs\ZayitLib\Html\index.html"
copy /Y dist\index.html "%DEST_HTML%"
if errorlevel 1 (
    echo ERROR: HTML copy failed! Check if destination path exists.
    exit /b 1
)
echo HTML copy complete!
echo.

echo [3/4] Syncing PDF.js files to C# project...
set "SRC_PDFJS=public\build"
set "DEST_PDFJS=..\Zayit-cs\ZayitLib\Html\pdfjs\build"
set "SRC_VIEWER=public\web\viewer.html"
set "DEST_VIEWER=..\Zayit-cs\ZayitLib\Html\pdfjs\web\viewer.html"

REM Create destination directory if it doesn't exist
if not exist "%DEST_PDFJS%" mkdir "%DEST_PDFJS%"

REM Copy all PDF.js build files
copy /Y "%SRC_PDFJS%\*.*" "%DEST_PDFJS%\"
if errorlevel 1 (
    echo ERROR: PDF.js files copy failed!
    exit /b 1
)

REM Copy viewer.html with Hebrew locale configuration
copy /Y "%SRC_VIEWER%" "%DEST_VIEWER%"
if errorlevel 1 (
    echo ERROR: PDF.js viewer.html copy failed!
    exit /b 1
)

echo PDF.js files and Hebrew locale configuration synced!
echo.

echo [4/4] Verifying deployment...
if exist "%DEST_HTML%" (
    echo SUCCESS: HTML deployed to %DEST_HTML%
) else (
    echo ERROR: HTML file not found at destination!
    exit /b 1
)

if exist "%DEST_PDFJS%\pdf.mjs" (
    echo SUCCESS: PDF.js files synced to %DEST_PDFJS%
) else (
    echo ERROR: PDF.js files not found at destination!
    exit /b 1
)

if exist "%DEST_VIEWER%" (
    echo SUCCESS: PDF.js viewer with Hebrew locale synced to %DEST_VIEWER%
) else (
    echo ERROR: PDF.js viewer.html not found at destination!
    exit /b 1
)
echo.

echo ========================================
echo Build and Deploy Complete!
echo ========================================
endlocal
