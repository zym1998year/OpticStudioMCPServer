param(
  [string]$Configuration = "Release",
  [string]$ZemaxRoot = $env:ZEMAX_ROOT
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\ZemaxMCP"

if ([string]::IsNullOrWhiteSpace($ZemaxRoot)) {
  throw "Set ZEMAX_ROOT to the installed OpticStudio folder before creating a release package."
}
foreach ($dll in "ZOSAPI.dll", "ZOSAPI_Interfaces.dll", "ZOSAPI_NetHelper.dll") {
  if (-not (Test-Path (Join-Path $ZemaxRoot $dll))) { throw "Missing $dll under ZEMAX_ROOT: $ZemaxRoot" }
}
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publish -ItemType Directory -Force | Out-Null

dotnet build "$root\src\ZemaxMCP.Server\ZemaxMCP.Server.csproj" -c $Configuration -p:ZEMAX_ROOT="$ZemaxRoot"
dotnet build "$root\src\ZemaxMCP.HttpBridge\ZemaxMCP.HttpBridge.csproj" -c $Configuration
dotnet build "$root\src\ZemaxMCP.Launcher\ZemaxMCP.Launcher.csproj" -c $Configuration
dotnet build "$root\src\ZemaxMCP.Installer\ZemaxMCP.Installer.csproj" -c $Configuration

$projects = "ZemaxMCP.Server", "ZemaxMCP.HttpBridge", "ZemaxMCP.Launcher", "ZemaxMCP.Installer"
foreach ($project in $projects) {
  # PDB files contain the absolute source path used by the release builder.
  # They are not needed to run the application and would expose that path in
  # user-facing exception logs.
  Copy-Item "$root\src\$project\bin\$Configuration\net48\*" $publish -Recurse -Force -Exclude "*.pdb", "*.xml", "ZOSAPI*.dll"
}
Copy-Item "$root\installer\Portable-Install.cmd" "$publish\Portable-Install.cmd" -Force
Copy-Item "$root\installer\Start-Zemax-MCP.cmd" "$publish\Start-Zemax-MCP.cmd" -Force
Copy-Item "$root\LICENSE" "$publish\LICENSE" -Force
Copy-Item "$root\THIRD_PARTY_NOTICES.md" "$publish\THIRD_PARTY_NOTICES.md" -Force
Compress-Archive "$publish\*" "$root\artifacts\ZemaxMCP-win-x64.zip" -Force
Write-Host "Release package: $root\artifacts\ZemaxMCP-win-x64.zip"
