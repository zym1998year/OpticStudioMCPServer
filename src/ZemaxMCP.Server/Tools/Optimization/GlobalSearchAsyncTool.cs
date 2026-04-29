using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly GlobalSearchState _state;

    public GlobalSearchAsyncTool(IZemaxSession session, GlobalSearchState state)
    {
        _session = session;
        _state = state;
    }

    public record GlobalSearchAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_global_search_async")]
    [Description(@"Start a non-blocking Global Search optimization. Returns immediately —
use zemax_global_search_status to poll progress and zemax_global_search_stop to cancel.
Same algorithm as zemax_global_search but the call returns within ~100ms regardless
of optimization length.")]
    public GlobalSearchAsyncResult Execute(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Number of solutions to save: 10, 20, 50, or 100")] int solutionsToSave = 10,
        [Description("Maximum runtime in seconds (0 for no limit)")] double timeoutSeconds = 60)
    {
        if (_state.IsRunning)
        {
            return new GlobalSearchAsyncResult(false, "Already running",
                "Global Search is already running. Use zemax_global_search_status / zemax_global_search_stop.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("GlobalSearchAsync",
                    new Dictionary<string, object?>
                    {
                        ["algorithm"] = algorithm,
                        ["cores"] = cores,
                        ["solutionsToSave"] = solutionsToSave,
                        ["timeoutSeconds"] = timeoutSeconds
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var mfe = system.MFE
                            ?? throw new InvalidOperationException("Merit Function Editor is not available");

                        var initialMerit = mfe.CalculateMeritFunction();
                        _state.SetRunning(algorithm, initialMerit);

                        var globalOpt = system.Tools?.OpenGlobalOptimization()
                            ?? throw new InvalidOperationException("Failed to open Global Optimization tool");

                        try
                        {
                            globalOpt.Algorithm = algorithm.ToUpper() switch
                            {
                                "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                                "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                                _ => OptimizationAlgorithm.DampedLeastSquares
                            };

                            globalOpt.NumberOfCores = cores > 0
                                ? Math.Min(cores, globalOpt.MaxCores)
                                : globalOpt.MaxCores;

                            globalOpt.NumberToSave = solutionsToSave switch
                            {
                                <= 10 => OptimizationSaveCount.Save_10,
                                <= 20 => OptimizationSaveCount.Save_20,
                                <= 50 => OptimizationSaveCount.Save_50,
                                _ => OptimizationSaveCount.Save_100
                            };

                            string terminationReason = "Unknown";
                            var stopwatch = Stopwatch.StartNew();
                            globalOpt.Run();

                            long timeoutMs = (long)(timeoutSeconds * 1000);
                            bool completed = false;

                            while (!completed)
                            {
                                Thread.Sleep(1000);
                                long now = stopwatch.ElapsedMilliseconds;

                                try
                                {
                                    double currentBest = globalOpt.CurrentMeritFunction(1);
                                    int validCount = 0;
                                    for (int i = 1; i <= solutionsToSave; i++)
                                    {
                                        var m = globalOpt.CurrentMeritFunction(i);
                                        if (m > 0 && m < double.MaxValue) validCount++;
                                        else break;
                                    }
                                    _state.UpdateProgress(currentBest, stopwatch.Elapsed.TotalSeconds, validCount);
                                }
                                catch { }

                                if (ct.IsCancellationRequested)
                                {
                                    terminationReason = "Cancelled";
                                    break;
                                }

                                if (timeoutSeconds > 0 && now >= timeoutMs)
                                {
                                    terminationReason = "Timeout";
                                    break;
                                }

                                try
                                {
                                    if (!globalOpt.IsRunning)
                                    {
                                        terminationReason = "Completed";
                                        completed = true;
                                    }
                                }
                                catch { }
                            }

                            globalOpt.Cancel();
                            globalOpt.WaitForCompletion();
                            stopwatch.Stop();

                            try
                            {
                                int finalValid = 0;
                                for (int i = 1; i <= solutionsToSave; i++)
                                {
                                    var m = globalOpt.CurrentMeritFunction(i);
                                    if (m > 0 && m < double.MaxValue) finalValid++;
                                    else break;
                                }
                                _state.UpdateProgress(globalOpt.CurrentMeritFunction(1),
                                                      stopwatch.Elapsed.TotalSeconds, finalValid);
                            }
                            catch { }

                            _state.SetCompleted(terminationReason);
                        }
                        finally
                        {
                            globalOpt.Close();
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

        return new GlobalSearchAsyncResult(true, null,
            $"Global Search started (algorithm={algorithm}, solutionsToSave={solutionsToSave}). " +
            "Use zemax_global_search_status / zemax_global_search_stop.");
    }
}
