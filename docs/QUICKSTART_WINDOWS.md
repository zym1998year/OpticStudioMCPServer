# Zemax MCP Windows quick start

1. Download and extract `ZemaxMCP-win-x64.zip` on the computer that has OpticStudio installed.
2. Double-click **Install.exe**. It installs into your user profile, creates a **Start Zemax MCP** desktop shortcut, and starts the service. No command line or administrator permission is needed for a local connection.
3. On later use, double-click **Start-Zemax-MCP.exe** (or the desktop shortcut). When a single OpticStudio version is installed, it is detected and the local endpoint starts automatically.
4. Click Codex, Claude, or Cursor in the small status window to add a `zemax-mcp` entry automatically.

For a second PC, tick **Share with a trusted LAN computer**, then use the displayed LAN endpoint in the client configuration. If Windows Firewall asks, allow the selected port only for the trusted private network. Do not expose an unauthenticated MCP endpoint to the public internet.

## Updates and logs

The launcher downloads and applies the current signed-off GitHub release with **Check updates**, then restarts itself. Releases are built automatically from version tags and include the server, HTTP bridge, launcher, and a rolling `logs` folder. Your OpticStudio installation and client configuration are retained.

## Development builds

Create `ZemaxPaths.props` with `ZEMAX_ROOT` set to an OpticStudio folder (or a copied, compatible ZOS-API folder), then run:

```powershell
./scripts/publish-windows.ps1
```

The script writes `artifacts/ZemaxMCP-win-x64.zip`.
