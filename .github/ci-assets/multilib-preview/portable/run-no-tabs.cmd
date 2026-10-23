@echo off
cd /d "%~dp0"
for %%F in (*.exe) do start "" "%%F" --plain
