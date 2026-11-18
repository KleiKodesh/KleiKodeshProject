@echo off
rem Klei Kodesh build menu.
rem   1. Auto  - release on GitHub: the Release workflow picks the version (Gemini),
rem              writes the notes, builds and publishes. Nothing is built here.
rem   2. Debug - local build, version 0.0.0, never released. Build\releases\
echo.
echo   1. Auto  (release on GitHub)
echo   2. Debug (local build, v0.0.0, no release)
echo.
choice /c 12 /n /m "Choose 1 or 2: "
if errorlevel 2 goto debug

gh workflow run release.yml --repo KleiKodesh/KleiKodeshProject
if errorlevel 1 (echo Could not start the release. Is gh logged in? & pause & exit /b 1)
echo Release started: https://github.com/KleiKodesh/KleiKodeshProject/actions/workflows/release.yml
pause
exit /b 0

:debug
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Version 0.0.0
pause
