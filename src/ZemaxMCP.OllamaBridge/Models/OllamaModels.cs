using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZemaxMCP.OllamaBridge.Models;

// Ollama API models
public class OllamaChatRequest
{
    [JsonProperty("model")]
    public string Model { get; set; } = "";

    [JsonProperty("messages")]
    public List<OllamaMessage> Messages { get; set; } = new();

    [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
    public List<OllamaTool>? Tools { get; set; }

    [JsonProperty("stream")]
    public bool Stream { get; set; } = false;

    [JsonProperty("options", NullValueHandling = NullValueHandling.Ignore)]
    public OllamaOptions? Options { get; set; }
}

public class OllamaOptions
{
    [JsonProperty("temperature")]
    public double Temperature { get; set; } = 0.1;

    [JsonProperty("num_ctx")]
    public int NumCtx { get; set; } = 8192;
}

public class OllamaMessage
{
    [JsonProperty("role")]
    public string Role { get; set; } = "";

    [JsonProperty("content")]
    public string Content { get; set; } = "";

    [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

public class OllamaToolCall
{
    [JsonProperty("function")]
    public OllamaFunctionCall Function { get; set; } = new();
}

public class OllamaFunctionCall
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("arguments")]
    public JObject Arguments { get; set; } = new();
}

public class OllamaTool
{
    [JsonProperty("type")]
    public string Type { get; set; } = "function";

    [JsonProperty("function")]
    public OllamaFunction Function { get; set; } = new();
}

public class OllamaFunction
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("parameters")]
    public JObject Parameters { get; set; } = new();
}

public class OllamaChatResponse
{
    [JsonProperty("message")]
    public OllamaMessage? Message { get; set; }

    [JsonProperty("done")]
    public bool Done { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

// MCP JSON-RPC models
public class JsonRpcRequest
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("method")]
    public string Method { get; set; } = "";

    [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
    public JObject? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonProperty("id")]
    public int? Id { get; set; }

    [JsonProperty("result")]
    public JToken? Result { get; set; }

    [JsonProperty("error")]
    public JsonRpcError? Error { get; set; }
}

public class JsonRpcError
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public class McpToolInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("inputSchema")]
    public JObject InputSchema { get; set; } = new();
}

public class McpToolsListResult
{
    [JsonProperty("tools")]
    public List<McpToolInfo> Tools { get; set; } = new();
}

public class McpToolCallResult
{
    [JsonProperty("content")]
    public List<McpContent> Content { get; set; } = new();

    [JsonProperty("isError")]
    public bool IsError { get; set; }
}

public class McpContent
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("text")]
    public string? Text { get; set; }
}
