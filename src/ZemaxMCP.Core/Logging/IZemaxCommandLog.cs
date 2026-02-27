namespace ZemaxMCP.Core.Logging;

/// <summary>
/// Interface for logging all commands sent to ZEMAX OpticStudio.
/// Provides a dedicated log file for debugging communication issues.
/// </summary>
public interface IZemaxCommandLog
{
    /// <summary>
    /// Log a command being sent to ZEMAX.
    /// </summary>
    /// <param name="commandName">Name of the command (e.g., "SetSurfaceRadius", "Optimize")</param>
    /// <param name="parameters">Dictionary of parameter names and values</param>
    void LogCommand(string commandName, IDictionary<string, object?>? parameters = null);

    /// <summary>
    /// Log a command result from ZEMAX.
    /// </summary>
    /// <param name="commandName">Name of the command</param>
    /// <param name="success">Whether the command succeeded</param>
    /// <param name="result">Result value or error message</param>
    /// <param name="elapsedMs">Execution time in milliseconds</param>
    void LogResult(string commandName, bool success, object? result = null, double elapsedMs = 0);

    /// <summary>
    /// Log an error that occurred during a command.
    /// </summary>
    /// <param name="commandName">Name of the command</param>
    /// <param name="exception">The exception that occurred</param>
    void LogError(string commandName, Exception exception);

    /// <summary>
    /// Log a raw ZOSAPI operation.
    /// </summary>
    /// <param name="operation">Description of the operation</param>
    void LogOperation(string operation);

    /// <summary>
    /// Path to the current log file.
    /// </summary>
    string LogFilePath { get; }
}
