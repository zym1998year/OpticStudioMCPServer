namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Hammer optimization state between calls to support progress
/// reporting and cancellation. Lifecycle method shape mirrors MultistartState
/// (Reset / SetRunning / UpdateMerit / SetCompleted / CreateCancellationToken /
/// RequestCancellation), but <see cref="HasState"/> semantics differ:
/// HasState here means "ran and reached a terminal state" (!IsRunning &&
/// terminationReason set), whereas MultistartState.HasState tracks the
/// "InitialLmDone" flag of its multi-phase lifecycle.
/// </summary>
public class HammerState
{
    private volatile bool _isRunning;
    private volatile string? _algorithm;
    private volatile int _improvements;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _currentMerit;
    private double _bestMerit;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public string? Algorithm => _algorithm;
    public int Improvements => _improvements;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double CurrentMerit { get { lock (_lock) return _currentMerit; } }
    public double BestMerit { get { lock (_lock) return _bestMerit; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _algorithm = null;
        _improvements = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _currentMerit = 0;
            _bestMerit = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(string algorithm, double initialMerit)
    {
        _isRunning = true;
        _algorithm = algorithm;
        _terminationReason = null;
        _errorMessage = null;
        _improvements = 0;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _currentMerit = initialMerit;
            _bestMerit = initialMerit;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateMerit(double currentMerit, double bestMerit, double runtimeSeconds, int improvements)
    {
        _improvements = improvements;
        lock (_lock)
        {
            _currentMerit = currentMerit;
            _bestMerit = bestMerit;
            _runtimeSeconds = runtimeSeconds;
        }
    }

    public void SetCompleted(string terminationReason, string? error = null)
    {
        _isRunning = false;
        _terminationReason = terminationReason;
        _errorMessage = error;
    }

    /// <summary>
    /// Create a fresh CancellationTokenSource for a new run. Disposes any
    /// previous one. Returns the token to be passed into the background task.
    /// </summary>
    public CancellationToken CreateCancellationToken()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    /// <summary>
    /// Request cancellation of the running Hammer optimization. The runner's
    /// loop will observe the cancellation in its next polling tick and call
    /// hammer.Cancel() before returning.
    /// </summary>
    public void RequestCancellation()
    {
        lock (_lock) { _cts?.Cancel(); }
    }

    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
    public Task? BackgroundTask => _backgroundTask;
}
