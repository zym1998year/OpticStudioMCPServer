using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class HammerAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly HammerState _state;

    public HammerAsyncTool(IZemaxSession session, HammerState state)
    {
        _session = session;
        _state = state;
    }

    public record HammerAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_hammer_async")]
    [Description(@"Start a non-blocking Hammer optimization. Returns immediately —
use zemax_hammer_status to poll progress and zemax_hammer_stop to cancel.
Same algorithm as zemax_hammer but the call returns within ~100ms regardless
of optimization length.")]
    public HammerAsyncResult Execute(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Target runtime in minutes")] double targetRuntimeMinutes = 5.0,
        [Description("Stagnation timeout in seconds (no improvement)")] double timeoutSeconds = 120,
        [Description("Reserved (currently no-op). Hammer is always run in continuous mode; this parameter is preserved for future automatic-mode tuning.")] bool automatic = true)
    {
        if (_state.IsRunning)
        {
            return new HammerAsyncResult(false, "Already running",
                "Hammer optimization is already running. " +
                "Use zemax_hammer_status to check progress or zemax_hammer_stop to cancel.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("HammerAsync",
                    new Dictionary<string, object?>
                    {
                        ["algorithm"] = algorithm,
                        ["cores"] = cores,
                        ["targetRuntimeMinutes"] = targetRuntimeMinutes,
                        ["timeoutSeconds"] = timeoutSeconds,
                        ["automatic"] = automatic
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var mfe = system.MFE
                            ?? throw new InvalidOperationException("Merit Function Editor is not available");

                        var initialMerit = mfe.CalculateMeritFunction();
                        _state.SetRunning(algorithm, initialMerit);

                        var hammer = system.Tools?.OpenHammerOptimization()
                            ?? throw new InvalidOperationException("Failed to open Hammer Optimization tool");

                        try
                        {
                            hammer.Algorithm = algorithm.ToUpper() switch
                            {
                                "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                                "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                                _ => OptimizationAlgorithm.DampedLeastSquares
                            };

                            hammer.NumberOfCores = cores > 0
                                ? Math.Min(cores, hammer.MaxCores)
                                : hammer.MaxCores;

                            string terminationReason = "Unknown";
                            var stopwatch = Stopwatch.StartNew();
                            hammer.Run();

                            double bestMerit = double.MaxValue;
                            long lastImprovedMs = 0;
                            long timeoutMs = (long)(timeoutSeconds * 1000);
                            long totalRuntimeMs = (long)(targetRuntimeMinutes * 60 * 1000);
                            int improvements = 0;

                            while (true)
                            {
                                Thread.Sleep(1000);

                                try
                                {
                                    double currentMerit = hammer.CurrentMeritFunction;
                                    long now = stopwatch.ElapsedMilliseconds;

                                    if (currentMerit < bestMerit)
                                    {
                                        if (bestMerit < double.MaxValue)
                                            improvements++;
                                        bestMerit = currentMerit;
                                        lastImprovedMs = now;
                                    }

                                    _state.UpdateMerit(currentMerit, bestMerit,
                                                       stopwatch.Elapsed.TotalSeconds, improvements);

                                    if (ct.IsCancellationRequested)
                                    {
                                        terminationReason = "Cancelled";
                                        break;
                                    }

                                    long idleMs = now - lastImprovedMs;
                                    if (idleMs >= timeoutMs)
                                    {
                                        terminationReason = improvements > 0 ? "Stagnation" : "NoImprovement";
                                        break;
                                    }

                                    if (now >= totalRuntimeMs)
                                    {
                                        terminationReason = "MaxRuntime";
                                        break;
                                    }
                                }
                                catch
                                {
                                    // Hammer may throw while running; ignore and keep polling
                                }
                            }

                            hammer.Cancel();
                            hammer.WaitForCompletion();
                            stopwatch.Stop();

                            // Final merit snapshot
                            try
                            {
                                _state.UpdateMerit(hammer.CurrentMeritFunction, bestMerit,
                                                   stopwatch.Elapsed.TotalSeconds, improvements);
                            }
                            catch { }

                            _state.SetCompleted(terminationReason);
                        }
                        finally
                        {
                            hammer.Close();
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

        return new HammerAsyncResult(true, null,
            $"Hammer optimization started (algorithm={algorithm}, " +
            $"targetRuntimeMinutes={targetRuntimeMinutes}). " +
            "Use zemax_hammer_status to check progress, zemax_hammer_stop to cancel.");
    }
}
