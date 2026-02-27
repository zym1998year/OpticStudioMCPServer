namespace ZemaxMCP.Server.Tools.Base;

public record ToolResult<T>
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public T? Data { get; init; }

    public static ToolResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static ToolResult<T> Fail(string error) => new() { Success = false, Error = error };
}
