using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ZemaxMCP.HttpBridge;

/// <summary>
/// A Windows-only, stateful HTTP-to-stdio MCP bridge.  It removes the Node.js /
/// supergateway dependency while keeping the established net48 ZOS-API server.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        var options = BridgeOptions.Parse(args);
        if (!File.Exists(options.ServerPath))
        {
            Console.Error.WriteLine("Server executable was not found: " + options.ServerPath);
            return 2;
        }

        Directory.CreateDirectory(options.LogDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(options.LogDirectory, "http-bridge-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            using (var bridge = new StdioMcpBridge(options))
            {
                bridge.RunAsync().GetAwaiter().GetResult();
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "HTTP bridge terminated unexpectedly");
            return 1;
        }
        finally { Log.CloseAndFlush(); }
    }
}

internal sealed class BridgeOptions
{
    public string ServerPath { get; private set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "ZemaxMCP.Server.exe");
    public string ZemaxRoot { get; private set; } = "";
    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 8000;
    public string Path { get; private set; } = "/mcp/";
    public string LogDirectory { get; private set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");

    public static BridgeOptions Parse(string[] args)
    {
        var result = new BridgeOptions();
        for (var i = 0; i < args.Length - 1; i += 2)
        {
            var value = args[i + 1];
            switch (args[i].ToLowerInvariant())
            {
                case "--server": result.ServerPath = value; break;
                case "--zemax-root": result.ZemaxRoot = value; break;
                case "--host": result.Host = value; break;
                case "--port": result.Port = int.Parse(value); break;
                case "--path": result.Path = value.TrimEnd('/') + "/"; break;
                case "--log-dir": result.LogDirectory = value; break;
            }
        }
        return result;
    }
}

internal sealed class StdioMcpBridge : IDisposable
{
    private readonly BridgeOptions _options;
    private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private Process? _server;
    private HttpListener? _listener;

    public StdioMcpBridge(BridgeOptions options) => _options = options;

    public async Task RunAsync()
    {
        StartServer();
        _listener = new HttpListener();
        // HTTP.SYS uses '+' as the all-interface prefix.  The launcher creates
        // its URL ACL only when the user explicitly enables LAN sharing.
        var listenerHost = _options.Host == "0.0.0.0" ? "+" : _options.Host;
        _listener.Prefixes.Add($"http://{listenerHost}:{_options.Port}{_options.Path}");
        _listener.Start();
        Log.Information("Zemax MCP HTTP endpoint listening at {Url}", _listener.Prefixes.FirstOrDefault());

        while (_listener.IsListening)
        {
            var context = await _listener.GetContextAsync().ConfigureAwait(false);
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private void StartServer()
    {
        var psi = new ProcessStartInfo(_options.ServerPath)
        {
            WorkingDirectory = System.IO.Path.GetDirectoryName(_options.ServerPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(_options.ZemaxRoot)) psi.EnvironmentVariables["ZEMAX_ROOT"] = _options.ZemaxRoot;
        _server = Process.Start(psi) ?? throw new InvalidOperationException("Unable to launch ZemaxMCP.Server.exe");
        _server.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.Warning("Server: {Message}", e.Data); };
        _server.BeginErrorReadLine();
        Log.Information("Started MCP stdio server with PID {Pid}", _server.Id);
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                context.Response.Close();
                return;
            }
            if (!context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.Close();
                return;
            }

            string request;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                request = await reader.ReadToEndAsync().ConfigureAwait(false);
            var json = JObject.Parse(request);
            var id = json["id"];
            var requestedSession = context.Request.Headers["Mcp-Session-Id"];
            if (!string.IsNullOrWhiteSpace(requestedSession) && !string.Equals(requestedSession, _sessionId, StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            await _requestLock.WaitAsync().ConfigureAwait(false);
            string? response;
            try
            {
                if (_server == null || _server.HasExited) throw new InvalidOperationException("The MCP server is not running.");
                await _server.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
                await _server.StandardInput.FlushAsync().ConfigureAwait(false);
                response = id == null ? null : await ReadResponseAsync(id).ConfigureAwait(false);
            }
            finally { _requestLock.Release(); }

            if (response == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                context.Response.Close();
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            // Streamable HTTP clients establish a session during initialize and
            // return this value on subsequent requests. The underlying stdio
            // server is intentionally stateful, so one bridge owns one session.
            if (string.Equals(json["method"]?.ToString(), "initialize", StringComparison.Ordinal))
                context.Response.Headers["Mcp-Session-Id"] = _sessionId;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HTTP request failed");
            var payload = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Zemax MCP bridge error\"},\"id\":null}");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    private async Task<string> ReadResponseAsync(JToken id)
    {
        while (true)
        {
            var line = await _server!.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            if (line == null) throw new EndOfStreamException("MCP server closed stdout.");
            var message = JObject.Parse(line);
            if (JToken.DeepEquals(message["id"], id)) return line;
            Log.Debug("Forwarded MCP notification: {Message}", line);
        }
    }

    public void Dispose()
    {
        try { _listener?.Close(); } catch { }
        try { if (_server != null && !_server.HasExited) _server.Kill(); } catch { }
        _server?.Dispose();
        _requestLock.Dispose();
    }
}
