@echo off
setlocal
set "edition=zh-CN"
if not "%~1"=="" set "edition=%~1"
if /I not "%edition%"=="zh-CN" if /I not "%edition%"=="en-US" (
    echo Usage: run.cmd [zh-CN^|en-US]
    exit /b 1
)
if not exist "%~dp0dist\WinStickies.%edition%.exe" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Language "%edition%"
if not exist "%~dp0dist\WinStickies.%edition%.exe" exit /b 1
start "" "%~dp0dist\WinStickies.%edition%.exe"
