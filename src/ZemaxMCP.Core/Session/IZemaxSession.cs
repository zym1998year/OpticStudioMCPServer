using ZOSAPI;

namespace ZemaxMCP.Core.Session;

public interface IZemaxSession : IDisposable
{
    bool IsConnected { get; }
    string? CurrentFilePath { get; }
    string? ZemaxDataDir { get; }

    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task<bool> ConnectAsync(ConnectionMode mode, int instanceId = 0, CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    Task<T> ExecuteAsync<T>(Func<IOpticalSystem, T> operation,
                            CancellationToken cancellationToken = default);

    Task ExecuteAsync(Action<IOpticalSystem> operation,
                      CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute an operation with command logging.
    /// </summary>
    /// <param name="commandName">Name of the command for logging</param>
    /// <param name="parameters">Parameters to log</param>
    /// <param name="operation">The operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<T> ExecuteAsync<T>(string commandName,
                            IDictionary<string, object?>? parameters,
                            Func<IOpticalSystem, T> operation,
                            CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute an operation with command logging.
    /// </summary>
    /// <param name="commandName">Name of the command for logging</param>
    /// <param name="parameters">Parameters to log</param>
    /// <param name="operation">The operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ExecuteAsync(string commandName,
                      IDictionary<string, object?>? parameters,
                      Action<IOpticalSystem> operation,
                      CancellationToken cancellationToken = default);

    Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> SaveFileAsync(string? filePath = null, CancellationToken cancellationToken = default);
    Task<bool> NewSystemAsync(CancellationToken cancellationToken = default);
}
