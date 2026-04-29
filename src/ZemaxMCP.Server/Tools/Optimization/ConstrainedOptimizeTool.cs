using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;

    public ConstrainedOptimizeTool(IZemaxSession session, ConstraintStore constraintStore)
    {
        _session = session;
        _constraintStore = constraintStore;
    }

    public record ConstrainedOptimizeResult(
        bool Success,
        string? Error,
        double InitialMerit,
        double FinalMerit,
        int Iterations,
        int Restarts,
        string Message
    );

    [McpServerTool(Name = "zemax_constrained_optimize")]
    [Description("Custom MCP-implemented bound-constrained Levenberg-Marquardt optimizer with Broyden rank-1 Jacobian updates. The LM algorithm runs entirely in the MCP server, using ZOSAPI only to get/set variable values and evaluate the merit function. Set variable constraints first with zemax_set_variable_constraints. NOT a built-in Zemax optimizer.")]
    public async Task<ConstrainedOptimizeResult> ExecuteAsync(
        [Description("Maximum iterations (default 200)")] int maxIterations = 200,
        [Description("Initial damping parameter mu (default 1e-3)")] double initialMu = 1e-3,
        [Description("Finite difference step size delta (default 1e-7)")] double delta = 1e-7,
        [Description("Use Broyden rank-1 Jacobian updates to reduce evaluations (default true)")] bool useBroydenUpdate = true,
        [Description("Maximum auto-restarts with fresh Jacobian when Broyden converges early (default 2)")] int maxRestarts = 2,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["maxIterations"] = maxIterations,
                ["initialMu"] = initialMu,
                ["delta"] = delta,
                ["useBroydenUpdate"] = useBroydenUpdate,
                ["maxRestarts"] = maxRestarts
            };

            var result = await _session.ExecuteAsync("ConstrainedOptimize", parameters, system =>
            {
                var scanner = new VariableScanner();
                var meritReader = new MeritFunctionReader();
                var lmOptimizer = new LMOptimizer(meritReader);

                var variables = scanner.ScanVariables(system);
                _constraintStore.ApplyConstraints(variables);

                // Use a single-element array so the lambda can mutate lastProgressMs.
                long[] lastProgressMs = { 0 };
                const long progressIntervalMs = 5000;
                var swProgress = Stopwatch.StartNew();

                Action<int, double, double, double> onProgress = (iter, merit, mu, runtimeSec) =>
                {
                    long now = swProgress.ElapsedMilliseconds;
                    if (now - lastProgressMs[0] >= progressIntervalMs)
                    {
                        progress?.Report(new ProgressNotificationValue
                        {
                            Progress = (float)iter,
                            Total = (float)maxIterations,
                            Message = $"constrained_optimize iter {iter}/{maxIterations}, " +
                                      $"merit: {merit:F6}, mu: {mu:F3}, runtime: {(int)runtimeSec}s"
                        });
                        lastProgressMs[0] = now;
                    }
                };

                var lmResult = lmOptimizer.Optimize(
                    system, variables, maxIterations, initialMu, delta,
                    useBroydenUpdate: useBroydenUpdate, maxRestarts: maxRestarts,
                    onProgress: onProgress,
                    cancellationToken: cancellationToken);

                return new ConstrainedOptimizeResult(
                    Success: lmResult.Success,
                    Error: lmResult.Success ? null : lmResult.Message,
                    InitialMerit: lmResult.InitialMerit,
                    FinalMerit: lmResult.FinalMerit,
                    Iterations: lmResult.Iterations,
                    Restarts: lmResult.Restarts,
                    Message: lmResult.Message
                );
            }, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            return new ConstrainedOptimizeResult(false, ex.Message, 0, 0, 0, 0, $"Error: {ex.Message}");
        }
    }
}
