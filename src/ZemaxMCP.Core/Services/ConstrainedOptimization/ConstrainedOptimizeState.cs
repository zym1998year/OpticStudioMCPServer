namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Constrained Optimization (zemax_constrained_optimize) state.
/// LM-specific fields: iteration / mu / restartsUsed. Lifecycle method shape
/// mirrors MultistartState (Reset / SetRunning / UpdateProgress / SetCompleted /
/// CreateCancellationToken / RequestCancellation), but <see cref="HasState"/>
/// semantics differ: HasState here means "ran and reached a terminal state"
/// (!IsRunning &amp;&amp; terminationReason set), whereas MultistartState.HasState
/// tracks the "InitialLmDone" flag of its multi-phase lifecycle.
/// </summary>
public class ConstrainedOptimizeState
{
    private volatile bool _isRunning;
    private volatile int _iteration;
    private volatile int _maxIterations;
    private volatile int _restartsUsed;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _currentMerit;
    private double _mu;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public int Iteration => _iteration;
    public int MaxIterations => _maxIterations;
    public int RestartsUsed => _restartsUsed;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double CurrentMerit { get { lock (_lock) return _currentMerit; } }
    public double Mu { get { lock (_lock) return _mu; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _iteration = 0;
        _maxIterations = 0;
        _restartsUsed = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _currentMerit = 0;
            _mu = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(int maxIterations, double initialMu, double initialMerit)
    {
        _isRunning = true;
        _maxIterations = maxIterations;
        _iteration = 0;
        _restartsUsed = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _currentMerit = initialMerit;
            _mu = initialMu;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateProgress(int iteration, double currentMerit, double mu, double runtimeSeconds, int restartsUsed)
    {
        _iteration = iteration;
        _restartsUsed = restartsUsed;
        lock (_lock)
        {
            _currentMerit = currentMerit;
            _mu = mu;
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
