namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Global Search optimization state. Mirrors MultistartState design.
/// </summary>
public class GlobalSearchState
{
    private volatile bool _isRunning;
    private volatile string? _algorithm;
    private volatile int _solutionsValid;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _bestMerit;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public string? Algorithm => _algorithm;
    public int SolutionsValid => _solutionsValid;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double BestMerit { get { lock (_lock) return _bestMerit; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _algorithm = null;
        _solutionsValid = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
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
        _solutionsValid = 0;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _bestMerit = initialMerit;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateProgress(double bestMerit, double runtimeSeconds, int solutionsValid)
    {
        _solutionsValid = solutionsValid;
        lock (_lock)
        {
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
