using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZOSAPI;
using ZemaxMCP.Core.Exceptions;
using ZemaxMCP.Core.Logging;

namespace ZemaxMCP.Core.Session;

public class ZemaxSession : IZemaxSession
{
    private readonly ILogger<ZemaxSession> _logger;
    private readonly IZemaxCommandLog _commandLog;
    private readonly ZemaxConnectionOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IZOSAPI_Application? _application;
    private IOpticalSystem? _primarySystem;
    private bool _disposed;
    private Task? _backgroundConnectTask;

    public bool IsConnected => _primarySystem != null;
    public bool IsConnecting => _backgroundConnectTask != null && !_backgroundConnectTask.IsCompleted;
    public string? CurrentFilePath { get; private set; }
    public string? ZemaxDataDir { get; private set; }

    public ZemaxSession(ILogger<ZemaxSession> logger, IOptions<ZemaxConnectionOptions> options, IZemaxCommandLog commandLog)
    {
        _logger = logger;
        _options = options.Value;
        _commandLog = commandLog;

        _commandLog.LogOperation($"ZemaxSession initialized. Log file: {_commandLog.LogFilePath}");
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return ConnectAsync(_options.Mode, _options.InstanceId, cancellationToken);
    }

    public async Task<bool> ConnectAsync(ConnectionMode mode, int instanceId = 0, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _commandLog.LogCommand("Connect", new Dictionary<string, object?>
        {
            ["Mode"] = mode.ToString(),
            ["TimeoutSeconds"] = _options.TimeoutSeconds,
            ["InstanceId"] = instanceId
        });

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                _logger.LogInformation("Already connected to OpticStudio");
                _commandLog.LogResult("Connect", true, "Already connected", sw.ElapsedMilliseconds);
                return true;
            }

            _logger.LogInformation("Connecting to OpticStudio in {Mode} mode", mode);

            // Initialize ZOSAPI
            bool isInitialized = ZOSAPI_NetHelper.ZOSAPI_Initializer.Initialize();
            if (!isInitialized)
            {
                throw new ZemaxConnectionException("Failed to initialize ZOSAPI. Ensure OpticStudio is installed.");
            }

            var connection = new ZOSAPI_Connection();

            if (mode == ConnectionMode.Standalone)
            {
                // Create standalone instance
                _application = connection.CreateNewApplication();
                if (_application == null)
                {
                    throw new ZemaxConnectionException("Failed to create standalone OpticStudio instance");
                }
            }
            else if (mode == ConnectionMode.Extension)
            {
                // Connect to running instance via Interactive Extension
                _application = connection.ConnectAsExtension(instanceId);
                if (_application == null)
                {
                    throw new ZemaxConnectionException(
                        $"Failed to connect to OpticStudio instance {instanceId}. " +
                        "Ensure OpticStudio is running and Interactive Extension mode is enabled " +
                        "(Programming > Interactive Extension).");
                }
            }

            if (!_application.IsValidLicenseForAPI)
            {
                throw new ZemaxConnectionException($"Invalid Zemax license: {_application.LicenseStatus}");
            }

            _primarySystem = _application.PrimarySystem;
            ZemaxDataDir = _application.ZemaxDataDir;

            if (_primarySystem == null)
            {
                throw new ZemaxConnectionException("Failed to get primary optical system");
            }

            _logger.LogInformation("Successfully connected to OpticStudio in {Mode} mode", mode);
            _commandLog.LogResult("Connect", true, $"Connected in {mode} mode", sw.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is not ZemaxConnectionException)
        {
            _logger.LogError(ex, "Failed to connect to OpticStudio");
            _commandLog.LogError("Connect", ex);
            await CleanupAsync();
            throw new ZemaxConnectionException("Failed to connect to OpticStudio", ex);
        }
        catch (ZemaxConnectionException ex)
        {
            _commandLog.LogError("Connect", ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        var sw = Stopwatch.StartNew();
        _commandLog.LogCommand("Disconnect", null);

        await _lock.WaitAsync();
        try
        {
            await CleanupAsync();
            _logger.LogInformation("Disconnected from OpticStudio");
            _commandLog.LogResult("Disconnect", true, "Disconnected", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _commandLog.LogError("Disconnect", ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void StartConnectInBackground(ConnectionMode mode = ConnectionMode.Standalone, int instanceId = 0)
    {
        if (IsConnected || IsConnecting) return;

        _logger.LogInformation("Starting background connection to OpticStudio in {Mode} mode", mode);
        _backgroundConnectTask = Task.Run(async () =>
        {
            try
            {
                await ConnectAsync(mode, instanceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background connection to OpticStudio failed");
            }
        });
    }

    public async Task<T> ExecuteAsync<T>(
        Func<IOpticalSystem, T> operation,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync("UnnamedOperation", null, operation, cancellationToken);
    }

    public async Task ExecuteAsync(
        Action<IOpticalSystem> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync("UnnamedOperation", null, operation, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        string commandName,
        IDictionary<string, object?>? parameters,
        Func<IOpticalSystem, T> operation,
        CancellationToken cancellationToken = default)
    {
        if (IsConnecting)
        {
            throw new ZemaxConnectionException(
                "OpticStudio is still connecting in the background. Please wait a moment and try again.");
        }

        var sw = Stopwatch.StartNew();
        _commandLog.LogCommand(commandName, parameters);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            var result = operation(_primarySystem!);
            _commandLog.LogResult(commandName, true, result, sw.ElapsedMilliseconds);
            return result;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _logger.LogError(ex, "COM error during Zemax operation: {Command}", commandName);
            _commandLog.LogError(commandName, ex);
            throw new ZemaxException($"Zemax operation failed: {commandName}", ex);
        }
        catch (Exception ex)
        {
            _commandLog.LogError(commandName, ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExecuteAsync(
        string commandName,
        IDictionary<string, object?>? parameters,
        Action<IOpticalSystem> operation,
        CancellationToken cancellationToken = default)
    {
        if (IsConnecting)
        {
            throw new ZemaxConnectionException(
                "OpticStudio is still connecting in the background. Please wait a moment and try again.");
        }

        var sw = Stopwatch.StartNew();
        _commandLog.LogCommand(commandName, parameters);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            operation(_primarySystem!);
            _commandLog.LogResult(commandName, true, null, sw.ElapsedMilliseconds);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _logger.LogError(ex, "COM error during Zemax operation: {Command}", commandName);
            _commandLog.LogError(commandName, ex);
            throw new ZemaxException($"Zemax operation failed: {commandName}", ex);
        }
        catch (Exception ex)
        {
            _commandLog.LogError(commandName, ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "OpenFile",
            new Dictionary<string, object?> { ["FilePath"] = filePath },
            system =>
            {
                var success = system.LoadFile(filePath, false);
                if (success)
                {
                    CurrentFilePath = filePath;
                    _logger.LogInformation("Opened file: {FilePath}", filePath);
                }
                return success;
            }, cancellationToken);
    }

    public async Task<bool> SaveFileAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        var path = filePath ?? CurrentFilePath;
        return await ExecuteAsync(
            "SaveFile",
            new Dictionary<string, object?> { ["FilePath"] = path },
            system =>
            {
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException("No file path specified and no current file");
                }

                system.SaveAs(path);
                CurrentFilePath = path;
                _logger.LogInformation("Saved file: {FilePath}", path);
                return true;
            }, cancellationToken);
    }

    public async Task<bool> NewSystemAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            "NewSystem",
            null,
            system =>
            {
                system.New(false);
                CurrentFilePath = null;
                _logger.LogInformation("Created new optical system");
                return true;
            }, cancellationToken);
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new ZemaxConnectionException("Not connected to OpticStudio");
        }
    }

    private Task CleanupAsync()
    {
        _primarySystem = null;

        if (_application != null)
        {
            try
            {
                _application.CloseApplication();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing OpticStudio application");
            }
            _application = null;
        }

        CurrentFilePath = null;
        ZemaxDataDir = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lock.Wait();
        try
        {
            CleanupAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
