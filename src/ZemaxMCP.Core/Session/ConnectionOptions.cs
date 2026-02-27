namespace ZemaxMCP.Core.Session;

public class ZemaxConnectionOptions
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.Standalone;
    public int InstanceId { get; set; } = 0;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryAttempts { get; set; } = 3;
    public string? DefaultFilePath { get; set; }
}

public enum ConnectionMode
{
    /// <summary>
    /// Create a new standalone OpticStudio instance (no UI)
    /// </summary>
    Standalone,

    /// <summary>
    /// Connect to an existing OpticStudio instance as an extension.
    /// Requires OpticStudio to be running with Interactive Extension enabled (Programming > Interactive Extension).
    /// </summary>
    Extension
}
