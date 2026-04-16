using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.System;

[McpServerToolType]
public class ConnectTool
{
    private readonly IZemaxSession _session;

    public ConnectTool(IZemaxSession session) => _session = session;

    public record ConnectResult(
        bool Success,
        string? Error,
        bool IsConnected,
        string? CurrentFile,
        string? Mode
    );

    [McpServerTool(Name = "zemax_connect")]
    [Description("Connect to Zemax OpticStudio. Modes: 'standalone' (default, launches headless instance) or 'extension' (connect to running OpticStudio with UI - requires Programming > Interactive Extension enabled in OpticStudio). Note: The server pre-connects in standalone mode at startup, so this tool is mainly for reconnection or switching modes.")]
    public Task<ConnectResult> ExecuteAsync(
        [Description("Connection mode: 'standalone' (headless, no UI) or 'extension' (attach to running OpticStudio with UI). Default: standalone.")]
        string mode = "standalone",
        [Description("OpticStudio instance ID for extension mode. Use 0 for the first available instance. Only used when mode is 'extension'.")]
        int instanceId = 0)
    {
        try
        {
            var connectionMode = mode.ToLowerInvariant() switch
            {
                "standalone" => ConnectionMode.Standalone,
                "extension" => ConnectionMode.Extension,
                _ => throw new ArgumentException($"Invalid mode '{mode}'. Use 'standalone' or 'extension'.")
            };

            // Already connected — return immediately
            if (_session.IsConnected)
            {
                return Task.FromResult(new ConnectResult(
                    Success: true,
                    Error: null,
                    IsConnected: true,
                    CurrentFile: _session.CurrentFilePath,
                    Mode: connectionMode.ToString()
                ));
            }

            // Background connection already in progress
            if (_session.IsConnecting)
            {
                return Task.FromResult(new ConnectResult(
                    Success: true,
                    Error: "Connection is already in progress. Please wait a moment and check zemax_status.",
                    IsConnected: false,
                    CurrentFile: null,
                    Mode: connectionMode.ToString()
                ));
            }

            // Start connection in background and return immediately
            _session.StartConnectInBackground(connectionMode, instanceId);

            return Task.FromResult(new ConnectResult(
                Success: true,
                Error: "Connection started in background. OpticStudio is initializing — check zemax_status shortly.",
                IsConnected: false,
                CurrentFile: null,
                Mode: connectionMode.ToString()
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ConnectResult(
                Success: false,
                Error: ex.Message,
                IsConnected: false,
                CurrentFile: null,
                Mode: null
            ));
        }
    }

    [McpServerTool(Name = "zemax_status")]
    [Description("Get the current connection status")]
    public Task<ConnectResult> GetStatusAsync()
    {
        return Task.FromResult(new ConnectResult(
            Success: true,
            Error: null,
            IsConnected: _session.IsConnected,
            CurrentFile: _session.CurrentFilePath,
            Mode: null
        ));
    }

    [McpServerTool(Name = "zemax_disconnect")]
    [Description("Disconnect from Zemax OpticStudio and close the application. Use this to cleanly close the session.")]
    public async Task<ConnectResult> DisconnectAsync()
    {
        try
        {
            await _session.DisconnectAsync();

            return new ConnectResult(
                Success: true,
                Error: null,
                IsConnected: _session.IsConnected,
                CurrentFile: _session.CurrentFilePath,
                Mode: null
            );
        }
        catch (Exception ex)
        {
            return new ConnectResult(
                Success: false,
                Error: ex.Message,
                IsConnected: _session.IsConnected,
                CurrentFile: _session.CurrentFilePath,
                Mode: null
            );
        }
    }

    [McpServerTool(Name = "zemax_restart")]
    [Description("Restart the Zemax OpticStudio connection. Disconnects the current session and creates a new connection. Use this when OpticStudio becomes unresponsive or times out.")]
    public async Task<ConnectResult> RestartAsync()
    {
        try
        {
            // First disconnect if connected
            if (_session.IsConnected)
            {
                await _session.DisconnectAsync();
            }

            // Wait a moment for cleanup
            await Task.Delay(500);

            // Reconnect
            var connected = await _session.ConnectAsync();

            return new ConnectResult(
                Success: connected,
                Error: connected ? null : "Failed to reconnect to OpticStudio",
                IsConnected: _session.IsConnected,
                CurrentFile: _session.CurrentFilePath,
                Mode: null
            );
        }
        catch (Exception ex)
        {
            return new ConnectResult(
                Success: false,
                Error: ex.Message,
                IsConnected: _session.IsConnected,
                CurrentFile: _session.CurrentFilePath,
                Mode: null
            );
        }
    }
}
