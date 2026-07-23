param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\ZemaxMCP"
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publish -ItemType Directory -Force | Out-Null

dotnet build "$root\src\ZemaxMCP.Server\ZemaxMCP.Server.csproj" -c $Configuration
dotnet build "$root\src\ZemaxMCP.HttpBridge\ZemaxMCP.HttpBridge.csproj" -c $Configuration
dotnet build "$root\src\ZemaxMCP.Launcher\ZemaxMCP.Launcher.csproj" -c $Configuration
dotnet build "$root\src\ZemaxMCP.Installer\ZemaxMCP.Installer.csproj" -c $Configuration

$projects = "ZemaxMCP.Server", "ZemaxMCP.HttpBridge", "ZemaxMCP.Launcher", "ZemaxMCP.Installer"
foreach ($project in $projects) {
  Copy-Item "$root\src\$project\bin\$Configuration\net48\*" $publish -Recurse -Force
}
Compress-Archive "$publish\*" "$root\artifacts\ZemaxMCP-win-x64.zip" -Force
Write-Host "Release package: $root\artifacts\ZemaxMCP-win-x64.zip"
