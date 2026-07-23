@echo off
setlocal

set "SOURCE=%~dp0"
set "TARGET=%LOCALAPPDATA%\ZemaxMCP"

if not exist "%TARGET%" mkdir "%TARGET%"
robocopy "%SOURCE%" "%TARGET%" /E /XD logs >nul

start "" "%TARGET%\Start-Zemax-MCP.exe"
endlocal
