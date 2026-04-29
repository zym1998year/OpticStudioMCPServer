namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Local Optimization (zemax_optimize) state. Mirrors MultistartState design.
/// </summary>
public class OptimizeState
{
    private volatile bool _isRunning;
    private volatile string? _algorithm;
    private volatile int _cyclesCompleted;
    private volatile int _cyclesRequested;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _currentMerit;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public string? Algorithm => _algorithm;
    public int CyclesCompleted => _cyclesCompleted;
    public int CyclesRequested => _cyclesRequested;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double CurrentMerit { get { lock (_lock) return _currentMerit; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _algorithm = null;
        _cyclesCompleted = 0;
        _cyclesRequested = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _currentMerit = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(string algorithm, int cyclesRequested, double initialMerit)
    {
        _isRunning = true;
        _algorithm = algorithm;
        _cyclesRequested = cyclesRequested;
        _cyclesCompleted = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _currentMerit = initialMerit;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateProgress(int cyclesCompleted, double currentMerit, double runtimeSeconds)
    {
        _cyclesCompleted = cyclesCompleted;
        lock (_lock)
        {
            _currentMerit = currentMerit;
            _runtimeSeconds = runtimeSeconds;
        }
    }

    public void SetCompleted(string terminationReason, string? error = null)
    {
        _isRunning = false;
        _terminationReason = terminationReason;
        _errorMessage = error;
    }

    public CancellationToken CreateCancellationToken()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    public void RequestCancellation()
    {
        lock (_lock) { _cts?.Cancel(); }
    }

    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
    public Task? BackgroundTask => _backgroundTask;
}
