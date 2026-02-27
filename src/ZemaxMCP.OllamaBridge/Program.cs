using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZemaxMCP.OllamaBridge;
using ZemaxMCP.OllamaBridge.Models;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Zemax OpticStudio + Ollama Bridge ===");
        Console.WriteLine();

        // Find MCP server executable
        var mcpServerPath = FindMcpServer();
        if (mcpServerPath == null)
        {
            Console.Error.WriteLine("ERROR: Could not find ZemaxMCP.Server.exe");
            Console.Error.WriteLine("Build the solution first, or pass the path as an argument:");
            Console.Error.WriteLine("  ZemaxMCP.OllamaBridge.exe <path-to-ZemaxMCP.Server.exe>");
            return 1;
        }

        // Determine Ollama model
        var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";
        var model = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("OLLAMA_MODEL");

        using var ollama = new OllamaClient(ollamaUrl);

        // If no model specified, let user pick
        if (string.IsNullOrEmpty(model))
        {
            model = await PickModelAsync(ollama);
            if (model == null) return 1;
        }

        Console.WriteLine($"Using model: {model}");
        Console.WriteLine($"MCP server:  {mcpServerPath}");
        Console.WriteLine();

        // Start MCP server
        Console.Write("Starting MCP server... ");
        using var mcp = new McpStdioClient(mcpServerPath);
        await mcp.InitializeAsync();
        Console.WriteLine("OK");

        // Discover tools
        Console.Write("Discovering tools... ");
        var mcpTools = await mcp.ListToolsAsync();
        Console.WriteLine($"found {mcpTools.Count} tools");
        Console.WriteLine();

        // Auto-connect to OpticStudio
        Console.Write("Connecting to OpticStudio (standalone)... ");
        bool isConnected = false;
        try
        {
            var connectResult = await mcp.CallToolAsync("zemax_connect",
                JObject.FromObject(new { mode = "standalone" }));
            isConnected = connectResult.Contains("\"isConnected\":true")
                       || connectResult.Contains("\"success\":true");
            Console.WriteLine(isConnected ? "OK" : "FAILED");
            if (!isConnected)
                Console.WriteLine($"  Connect result: {connectResult}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
        }

        // Convert MCP tools to Ollama format
        var ollamaTools = mcpTools.Select(t => new OllamaTool
        {
            Type = "function",
            Function = new OllamaFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.InputSchema
            }
        }).ToList();

        // Build system prompt
        var systemPrompt = BuildSystemPrompt(mcpTools, isConnected);

        // Chat loop
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        Console.WriteLine("Ready! Type your message (or 'quit' to exit, 'tools' to list tools).");
        Console.WriteLine(new string('-', 60));

        while (true)
        {
            Console.Write("\nYou: ");
            var input = Console.ReadLine();
            if (input == null || input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Trim().Equals("tools", StringComparison.OrdinalIgnoreCase))
            {
                PrintTools(mcpTools);
                continue;
            }

            messages.Add(new OllamaMessage { Role = "user", Content = input });

            // Chat loop with tool calling
            while (true)
            {
                var request = new OllamaChatRequest
                {
                    Model = model,
                    Messages = messages,
                    Tools = ollamaTools,
                    Stream = false,
                    Options = new OllamaOptions { Temperature = 0.1, NumCtx = 8192 }
                };

                OllamaChatResponse response;
                try
                {
                    response = await ollama.ChatAsync(request);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[ERROR] Ollama: {ex.Message}");
                    break;
                }

                if (response.Error != null)
                {
                    Console.WriteLine($"\n[ERROR] {response.Error}");
                    break;
                }

                var msg = response.Message;
                if (msg == null) break;

                messages.Add(msg);

                // If the model wants to call tools
                if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    foreach (var toolCall in msg.ToolCalls)
                    {
                        var toolName = toolCall.Function.Name;
                        var toolArgs = toolCall.Function.Arguments;

                        Console.WriteLine($"\n[Calling tool: {toolName}]");

                        string result;
                        try
                        {
                            result = await mcp.CallToolAsync(toolName, toolArgs);
                        }
                        catch (Exception ex)
                        {
                            result = $"Error calling tool: {ex.Message}";
                        }

                        // Truncate very long results for display
                        var displayResult = result.Length > 500
                            ? result.Substring(0, 500) + "..."
                            : result;
                        Console.WriteLine($"[Result: {displayResult}]");

                        messages.Add(new OllamaMessage
                        {
                            Role = "tool",
                            Content = result
                        });
                    }
                    // Continue the loop so the model can process tool results
                    continue;
                }

                // No tool calls - just print the response
                if (!string.IsNullOrEmpty(msg.Content))
                    Console.WriteLine($"\nAssistant: {msg.Content}");

                break;
            }
        }

        Console.WriteLine("\nGoodbye!");
        return 0;
    }

    static string? FindMcpServer()
    {
        // Check command line arg first
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var fullPath = Path.GetFullPath(args[1]);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Check environment variable
        var envPath = Environment.GetEnvironmentVariable("MCP_SERVER_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // Look relative to this executable
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "ZemaxMCP.Server", "bin", "Debug", "net48", "ZemaxMCP.Server.exe"),
            Path.Combine(baseDir, "..", "ZemaxMCP.Server", "bin", "Release", "net48", "ZemaxMCP.Server.exe"),
            Path.Combine(baseDir, "ZemaxMCP.Server.exe"),
            // When running from src/ZemaxMCP.OllamaBridge/bin/Debug/net48
            Path.Combine(baseDir, "..", "..", "..", "..", "ZemaxMCP.Server", "bin", "Debug", "net48", "ZemaxMCP.Server.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "ZemaxMCP.Server", "bin", "Release", "net48", "ZemaxMCP.Server.exe"),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    static async Task<string?> PickModelAsync(OllamaClient ollama)
    {
        List<string> models;
        try
        {
            models = await ollama.ListModelsAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: Could not connect to Ollama: {ex.Message}");
            Console.Error.WriteLine("Make sure Ollama is running (ollama serve).");
            return null;
        }

        if (models.Count == 0)
        {
            Console.Error.WriteLine("ERROR: No models found. Pull a model first: ollama pull llama3.1");
            return null;
        }

        Console.WriteLine("Available models:");
        for (int i = 0; i < models.Count; i++)
            Console.WriteLine($"  {i + 1}. {models[i]}");

        Console.Write($"\nSelect model [1-{models.Count}]: ");
        var selection = Console.ReadLine();
        if (int.TryParse(selection, out int idx) && idx >= 1 && idx <= models.Count)
            return models[idx - 1];

        Console.Error.WriteLine("Invalid selection.");
        return null;
    }

    static string BuildSystemPrompt(List<McpToolInfo> tools, bool isConnected)
    {
        var toolList = string.Join("\n", tools.Select(t => $"  - {t.Name}: {t.Description.Substring(0, Math.Min(80, t.Description.Length))}"));

        var connectionStatus = isConnected
            ? "You are ALREADY CONNECTED to OpticStudio. Do NOT call zemax_connect again."
            : "You are NOT connected. You MUST call zemax_connect with mode=standalone BEFORE any other tool.";

        return $@"You are an optical engineering assistant. You control Zemax OpticStudio by calling tools.
You MUST respond in English only.

CONNECTION STATUS: {connectionStatus}

CRITICAL RULES:
1. When the user asks you to do something, CALL THE APPROPRIATE TOOL. Do NOT just describe what you would do.
2. NEVER say ""I would call..."" or ""You need to..."" - just call the tool directly.
3. Always use standalone mode for zemax_connect unless the user explicitly says extension mode.
4. If a tool returns an error, tell the user what went wrong and suggest a fix.

WORKFLOW:
- To connect: call zemax_connect with mode=""standalone""
- To open a file: call zemax_open_file with filePath
- To see the system: call zemax_get_system
- To analyze: call the appropriate analysis tool (zemax_spot_diagram, zemax_fft_mtf, etc.)
- To optimize: call zemax_optimize, zemax_hammer, or zemax_global_search

AVAILABLE TOOLS:
{toolList}";
    }

    static void PrintTools(List<McpToolInfo> tools)
    {
        Console.WriteLine($"\nAvailable tools ({tools.Count}):");
        foreach (var tool in tools.OrderBy(t => t.Name))
            Console.WriteLine($"  {tool.Name,-45} {tool.Description.Substring(0, Math.Min(60, tool.Description.Length))}...");
    }
}
