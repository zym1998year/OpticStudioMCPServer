using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;
    private readonly ConstrainedOptimizeState _state;

    public ConstrainedOptimizeAsyncTool(
        IZemaxSession session,
        ConstraintStore constraintStore,
        ConstrainedOptimizeState state)
    {
        _session = session;
        _constraintStore = constraintStore;
        _state = state;
    }

    public record ConstrainedOptimizeAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_constrained_optimize_async")]
    [Description(@"Start a non-blocking constrained LM optimization. Returns immediately —
use zemax_constrained_optimize_status to poll progress and
zemax_constrained_optimize_stop to cancel.")]
    public ConstrainedOptimizeAsyncResult Execute(
        [Description("Maximum iterations (default 200)")] int maxIterations = 200,
        [Description("Initial damping parameter mu (default 1e-3)")] double initialMu = 1e-3,
        [Description("Finite difference step size delta (default 1e-7)")] double delta = 1e-7,
        [Description("Use Broyden rank-1 Jacobian updates (default true)")] bool useBroydenUpdate = true,
        [Description("Maximum auto-restarts (default 2)")] int maxRestarts = 2)
    {
        if (_state.IsRunning)
        {
            return new ConstrainedOptimizeAsyncResult(false, "Already running",
                "Constrained Optimize is already running. " +
                "Use zemax_constrained_optimize_status / zemax_constrained_optimize_stop.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("ConstrainedOptimizeAsync",
                    new Dictionary<string, object?>
                    {
                        ["maxIterations"] = maxIterations,
                        ["initialMu"] = initialMu,
                        ["delta"] = delta,
                        ["useBroydenUpdate"] = useBroydenUpdate,
                        ["maxRestarts"] = maxRestarts
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var scanner = new VariableScanner();
                        var meritReader = new MeritFunctionReader();
                        var lmOptimizer = new LMOptimizer(meritReader);

                        var variables = scanner.ScanVariables(system);
                        _constraintStore.ApplyConstraints(variables);

                        // Read initial merit via CalculateMeritFunction — same pattern as sync tool.
                        var initialMerit = system.MFE?.CalculateMeritFunction()
                            ?? throw new InvalidOperationException("Cannot read initial merit");

                        _state.SetRunning(maxIterations, initialMu, initialMerit);

                        var stopwatch = Stopwatch.StartNew();

                        Action<int, double, double, double> onProgress = (iter, merit, mu, runtimeSec) =>
                        {
                            // RestartsUsed is not exposed by the onProgress callback signature;
                            // pass 0 during iteration — final value is taken from lmResult.Restarts.
                            _state.UpdateProgress(iter, merit, mu, runtimeSec, 0);
                        };

                        var lmResult = lmOptimizer.Optimize(
                            system, variables, maxIterations, initialMu, delta,
                            useBroydenUpdate: useBroydenUpdate, maxRestarts: maxRestarts,
                            onProgress: onProgress,
                            cancellationToken: ct);

                        stopwatch.Stop();

                        _state.UpdateProgress(
                            lmResult.Iterations,
                            lmResult.FinalMerit,
                            0,   // final mu not stored on result
                            stopwatch.Elapsed.TotalSeconds,
                            lmResult.Restarts);

                        // Preserve LM-internal failure message. ct.IsCancellationRequested
                        // wins over lmResult.Success because the LM catches OperationCanceledException
                        // and returns Success=false with a generic "Operation was canceled" message;
                        // we want clearer semantics for the user.
                        string termReason = ct.IsCancellationRequested
                            ? "Cancelled"
                            : (lmResult.Success ? "Completed" : "Error");
                        string? error = (ct.IsCancellationRequested || lmResult.Success)
                            ? null
                            : lmResult.Message;
                        _state.SetCompleted(termReason, error);
                    }, ct);
            }
            catch (OperationCanceledException)
            {
                _state.SetCompleted("Cancelled", "Cancelled by user");
            }
            catch (Exception ex)
            {
                _state.SetCompleted("Error", ex.Message);
            }
        });

        _state.SetBackgroundTask(task);

        return new ConstrainedOptimizeAsyncResult(true, null,
            $"Constrained Optimize started (maxIterations={maxIterations}). " +
            "Use zemax_constrained_optimize_status / zemax_constrained_optimize_stop.");
    }
}
