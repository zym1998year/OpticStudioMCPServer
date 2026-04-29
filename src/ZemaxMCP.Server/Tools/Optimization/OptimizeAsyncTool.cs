using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly OptimizeState _state;

    public OptimizeAsyncTool(IZemaxSession session, OptimizeState state)
    {
        _session = session;
        _state = state;
    }

    public record OptimizeAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_optimize_async")]
    [Description(@"Start a non-blocking local optimization. Returns immediately —
use zemax_optimize_status to poll progress and zemax_optimize_stop to cancel.")]
    public OptimizeAsyncResult Execute(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of cycles (0 for automatic)")] int cycles = 0)
    {
        if (_state.IsRunning)
        {
            return new OptimizeAsyncResult(false, "Already running",
                "Optimize is already running. Use zemax_optimize_status / zemax_optimize_stop.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("OptimizeAsync",
                    new Dictionary<string, object?>
                    {
                        ["algorithm"] = algorithm,
                        ["cycles"] = cycles
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var mfe = system.MFE
                            ?? throw new InvalidOperationException("Merit Function Editor is not available");

                        var initialMerit = mfe.CalculateMeritFunction();
                        _state.SetRunning(algorithm, cycles, initialMerit);

                        var optimizer = system.Tools?.OpenLocalOptimization()
                            ?? throw new InvalidOperationException("Failed to open Local Optimization tool");

                        try
                        {
                            optimizer.Algorithm = algorithm.ToUpper() switch
                            {
                                "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                                "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                                _ => OptimizationAlgorithm.DampedLeastSquares
                            };
                            optimizer.Cycles = OptimizeTool.MapCyclesToEnum(cycles);

                            string terminationReason = "Unknown";
                            var stopwatch = Stopwatch.StartNew();
                            int pollCycles = 0;
                            optimizer.Run();

                            bool completed = false;
                            while (!completed)
                            {
                                Thread.Sleep(1000);
                                pollCycles++;

                                try
                                {
                                    double currentMerit = optimizer.CurrentMeritFunction;
                                    _state.UpdateProgress(pollCycles, currentMerit,
                                                          stopwatch.Elapsed.TotalSeconds);
                                }
                                catch { }

                                if (ct.IsCancellationRequested)
                                {
                                    terminationReason = "Cancelled";
                                    break;
                                }

                                try
                                {
                                    if (!optimizer.IsRunning)
                                    {
                                        terminationReason = "Completed";
                                        completed = true;
                                    }
                                }
                                catch { }
                            }

                            optimizer.Cancel();
                            optimizer.WaitForCompletion();
                            stopwatch.Stop();

                            try
                            {
                                _state.UpdateProgress(pollCycles,
                                                      optimizer.CurrentMeritFunction,
                                                      stopwatch.Elapsed.TotalSeconds);
                            }
                            catch { }

                            _state.SetCompleted(terminationReason);
                        }
                        finally
                        {
                            optimizer.Close();
                        }
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

        return new OptimizeAsyncResult(true, null,
            $"Optimize started (algorithm={algorithm}, cycles={cycles}). " +
            "Use zemax_optimize_status / zemax_optimize_stop.");
    }
}
