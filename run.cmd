@echo off
setlocal
if not exist "%~dp0dist\WinStickies.exe" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if not exist "%~dp0dist\WinStickies.exe" exit /b 1
start "" "%~dp0dist\WinStickies.exe"
