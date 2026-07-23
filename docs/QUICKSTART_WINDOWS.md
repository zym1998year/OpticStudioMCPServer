# Zemax MCP Windows quick start

1. Download and extract `ZemaxMCP-win-x64.zip` on the computer that has OpticStudio installed.
2. Double-click **Install.exe**. It installs into your user profile, creates a **Start Zemax MCP** desktop shortcut, and starts the service. No command line or administrator permission is needed for a local connection.
3. On later use, double-click **Start-Zemax-MCP.exe** (or the desktop shortcut). The newest detected OpticStudio version is selected and the local endpoint starts automatically; the window lets you switch versions if needed. A second launch simply shows that the app is already running.
4. Click Codex, Claude, or Cursor in the small status window to add a `zemax-mcp` entry automatically.

For a second PC, tick **Share with a trusted LAN computer**, then use the displayed LAN endpoint in the client configuration. If Windows Firewall asks, allow the selected port only for the trusted private network. Do not expose an unauthenticated MCP endpoint to the public internet.

On the AI-client computer, extract the same release, double-click `Install.exe`, paste the endpoint copied from the OpticStudio computer into **Remote MCP URL**, then click **Configure installed AI clients**. This is the only extra step for a two-computer setup and requires no terminal or manual file editing.

## Updates and logs

The launcher downloads and applies the current signed-off GitHub release with **Check updates**, then restarts itself. Releases are built automatically from version tags and include the server, HTTP bridge, launcher, and a rolling `logs` folder. Your OpticStudio installation and client configuration are retained.

## Development builds

Create `ZemaxPaths.props` with `ZEMAX_ROOT` set to an OpticStudio folder (or a copied, compatible ZOS-API folder), then run:

```powershell
./scripts/publish-windows.ps1
```

The script writes `artifacts/ZemaxMCP-win-x64.zip`.
