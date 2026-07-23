# Zemax MCP Windows quick start

1. Download and extract `ZemaxMCP-win-x64.zip` on the computer that has OpticStudio installed.
2. Double-click **Install.exe**. It installs into your user profile, creates a **Start Zemax MCP** desktop shortcut, and starts the service. No command line or administrator permission is needed for a local connection.
3. On later use, double-click **Start-Zemax-MCP.exe** (or the desktop shortcut). The newest detected OpticStudio version is selected and the local endpoint starts automatically; the window lets you switch versions if needed. A second launch simply shows that the app is already running.
4. Click Codex, Claude, or Cursor in the small status window to add a `zemax-mcp` entry automatically.

If your organization blocks `Install.exe` but permits batch files, double-click `Portable-Install.cmd` instead. It performs the same per-user copy and starts the launcher. This is a convenience fallback, not a way to bypass corporate security policy: if Windows also blocks the application executable, request an IT allow-list or a company code-signing certificate.

For a second PC, tick **Share with a trusted LAN computer**, then use the displayed LAN endpoint in the client configuration. If Windows Firewall asks, allow the selected port only for the trusted private network. Do not expose an unauthenticated MCP endpoint to the public internet.

On the AI-client computer, extract the same release, double-click `Install.exe`, paste the endpoint copied from the OpticStudio computer into **Remote MCP URL**, then click **Configure installed AI clients**. This is the only extra step for a two-computer setup and requires no terminal or manual file editing.

## Updates and logs

The launcher downloads and applies the current GitHub release with **Check updates**, then restarts itself. Each release includes the server, HTTP bridge, launcher, and a rolling `logs` folder. Your OpticStudio installation and client configuration are retained.

Use **Open logs** in the launcher for both bridge and server diagnostics; no command prompt is needed to locate or inspect logs.

## Development builds

Release maintainers create the ZIP on a Windows computer that has OpticStudio installed (the ZOS-API DLLs are proprietary and cannot be built on GitHub-hosted runners). Set `ZEMAX_ROOT` to the OpticStudio folder, then run:

```powershell
./scripts/publish-windows.ps1
```

The script writes `artifacts/ZemaxMCP-win-x64.zip`.
