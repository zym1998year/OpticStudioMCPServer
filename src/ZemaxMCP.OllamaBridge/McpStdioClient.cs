using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZemaxMCP.OllamaBridge.Models;

namespace ZemaxMCP.OllamaBridge;

/// <summary>
/// Communicates with an MCP server over stdio using JSON-RPC 2.0.
/// </summary>
public class McpStdioClient : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private int _requestId;
    private readonly object _lock = new();

    public McpStdioClient(string executablePath, string? arguments = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? "",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start MCP server process");

        _writer = _process.StandardInput;
        _reader = _process.StandardOutput;

        // Log stderr in background
        Task.Run(async () =>
        {
            while (!_process.HasExited)
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line != null)
                    Console.Error.WriteLine($"[MCP stderr] {line}");
            }
        });
    }

    public async Task InitializeAsync()
    {
        var result = await SendRequestAsync("initialize", JObject.FromObject(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "ollama-bridge", version = "1.0.0" }
        }));

        // Send initialized notification (no id, no response expected)
        var notification = new { jsonrpc = "2.0", method = "notifications/initialized" };
        await WriteMessageAsync(JsonConvert.SerializeObject(notification));
    }

    public async Task<List<McpToolInfo>> ListToolsAsync()
    {
        var result = await SendRequestAsync("tools/list", null);
        var toolsResult = result?.ToObject<McpToolsListResult>();
        return toolsResult?.Tools ?? new List<McpToolInfo>();
    }

    public async Task<string> CallToolAsync(string toolName, JObject arguments)
    {
        var result = await SendRequestAsync("tools/call", JObject.FromObject(new
        {
            name = toolName,
            arguments = arguments
        }));

        var callResult = result?.ToObject<McpToolCallResult>();
        if (callResult == null)
            return "No result";

        var texts = callResult.Content
            .Where(c => c.Type == "text" && c.Text != null)
            .Select(c => c.Text!);

        return string.Join("\n", texts);
    }

    private async Task<JToken?> SendRequestAsync(string method, JObject? @params)
    {
        int id;
        lock (_lock) { id = ++_requestId; }

        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = @params
        };

        var json = JsonConvert.SerializeObject(request, Formatting.None);
        await WriteMessageAsync(json);

        // Read responses until we get one matching our id
        while (true)
        {
            var responseLine = await ReadMessageAsync();
            if (responseLine == null)
                throw new InvalidOperationException("MCP server closed connection");

            // Skip non-JSON lines (ZOSAPI may write to raw stdout)
            var trimmed = responseLine.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{')
                continue;

            JsonRpcResponse? response;
            try
            {
                response = JsonConvert.DeserializeObject<JsonRpcResponse>(responseLine);
            }
            catch (JsonException)
            {
                // Not valid JSON-RPC, skip it
                continue;
            }
            if (response == null) continue;

            // Skip notifications (no id)
            if (response.Id == null) continue;

            if (response.Id == id)
            {
                if (response.Error != null)
                    throw new InvalidOperationException($"MCP error: {response.Error.Message}");
                return response.Result;
            }
        }
    }

    private async Task WriteMessageAsync(string json)
    {
        await _writer.WriteLineAsync(json);
        await _writer.FlushAsync();
    }

    private async Task<string?> ReadMessageAsync()
    {
        return await _reader.ReadLineAsync();
    }

    public void Dispose()
    {
        try
        {
            _writer.Dispose();
            _reader.Dispose();
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(3000);
            }
            _process.Dispose();
        }
        catch { }
    }
}
