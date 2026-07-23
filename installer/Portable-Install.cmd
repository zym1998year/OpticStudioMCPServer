@echo off
setlocal

set "SOURCE=%~dp0"
set "TARGET=%LOCALAPPDATA%\ZemaxMCP"

if not exist "%TARGET%" mkdir "%TARGET%"
robocopy "%SOURCE%" "%TARGET%" /E /XD logs >nul

if exist "%TARGET%\Start-Zemax-MCP.exe" (
  start "" "%TARGET%\Start-Zemax-MCP.exe"
  endlocal
  exit /b 0
)

rem A managed device can prohibit copying executables into LocalAppData.
rem The extracted release remains a safe portable fallback in that case.
if exist "%SOURCE%\Start-Zemax-MCP.exe" (
  echo Per-user installation was blocked. Starting the portable release instead.
  start "" "%SOURCE%\Start-Zemax-MCP.exe"
  endlocal
  exit /b 0
)

echo Could not find Start-Zemax-MCP.exe beside this script.
echo Ensure every file in ZemaxMCP-win-x64.zip was extracted to one folder.
pause
endlocal
