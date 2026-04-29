using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;
using ZOSAPI.Tools.Optimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeTool
{
    private readonly IZemaxSession _session;

    public OptimizeTool(IZemaxSession session) => _session = session;

    public record OptimizeResult(
        bool Success,
        string? Error,
        double InitialMerit,
        double FinalMerit,
        double Improvement,
        int CyclesCompleted,
        string Algorithm,
        string TerminationReason
    );

    [McpServerTool(Name = "zemax_optimize")]
    [Description("Run optimization on the current optical system")]
    public async Task<OptimizeResult> ExecuteAsync(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of cycles (0 for automatic)")] int cycles = 0,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["algorithm"] = algorithm,
                ["cycles"] = cycles
            };

            var result = await _session.ExecuteAsync("Optimize", parameters, system =>
            {
                if (system == null)
                {
                    throw new InvalidOperationException("Optical system is not available");
                }

                var mfe = system.MFE;
                if (mfe == null)
                {
                    throw new InvalidOperationException("Merit Function Editor is not available");
                }

                var initialMerit = mfe.CalculateMeritFunction();

                var optimizer = system.Tools?.OpenLocalOptimization();
                if (optimizer == null)
                {
                    throw new InvalidOperationException("Failed to open Local Optimization tool");
                }

                try
                {
                    // Set algorithm
                    optimizer.Algorithm = algorithm.ToUpper() switch
                    {
                        "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                        "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                        _ => OptimizationAlgorithm.DampedLeastSquares
                    };

                    // Map cycles to appropriate enum value
                    // ZOSAPI OptimizationCycles enum has: Automatic, Fixed_1_Cycle, Fixed_5_Cycles,
                    // Fixed_10_Cycles, Fixed_50_Cycles, Infinite
                    optimizer.Cycles = MapCyclesToEnum(cycles);

                    // Get the actual cycle count for reporting
                    int expectedCycles = GetExpectedCycleCount(cycles);

                    // Run optimization non-blocking, then poll for completion.
                    var stopwatch = Stopwatch.StartNew();
                    optimizer.Run();

                    string terminationReason = "Completed";
                    long lastProgressMs = 0;
                    const long progressIntervalMs = 5000;
                    bool completed = false;

                    while (!completed)
                    {
                        Thread.Sleep(1000);
                        long now = stopwatch.ElapsedMilliseconds;

                        // Emit progress every 5s; SDK is a no-op when client did
                        // not provide a progressToken.
                        if (now - lastProgressMs >= progressIntervalMs)
                        {
                            double currentMerit = 0;
                            try { currentMerit = optimizer.CurrentMeritFunction; } catch { }
                            progress?.Report(new ProgressNotificationValue
                            {
                                Progress = (float)stopwatch.Elapsed.TotalSeconds,
                                Total = null, // total runtime unknown for local optimize
                                Message = $"optimize running for {(int)stopwatch.Elapsed.TotalSeconds}s, " +
                                          $"current merit: {currentMerit:F6}"
                            });
                            lastProgressMs = now;
                        }

                        // Honor client-side cancellation.
                        if (cancellationToken.IsCancellationRequested)
                        {
                            terminationReason = "Cancelled";
                            optimizer.Cancel();
                            optimizer.WaitForCompletion();
                            completed = true;
                            break;
                        }

                        // Check natural completion via IsRunning; if property doesn't
                        // exist on this ZOSAPI version, the catch keeps polling.
                        try
                        {
                            if (!optimizer.IsRunning)
                            {
                                terminationReason = "Completed";
                                completed = true;
                                break;
                            }
                        }
                        catch { /* keep polling */ }
                    }
                    stopwatch.Stop();

                    // Calculate final merit
                    var finalMerit = mfe.CalculateMeritFunction();

                    // Refine termination reason based on improvement
                    if (terminationReason == "Completed")
                    {
                        if (Math.Abs(finalMerit - initialMerit) < 1e-10)
                        {
                            terminationReason = "No improvement (possibly converged or no variables)";
                        }
                        else if (finalMerit < initialMerit)
                        {
                            terminationReason = "Converged";
                        }
                    }

                    return new OptimizeResult(
                        Success: true,
                        Error: null,
                        InitialMerit: initialMerit,
                        FinalMerit: finalMerit,
                        Improvement: initialMerit - finalMerit,
                        CyclesCompleted: expectedCycles,
                        Algorithm: algorithm,
                        TerminationReason: terminationReason
                    );
                }
                finally
                {
                    optimizer.Close();
                }
            }, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            return new OptimizeResult(false, ex.Message, 0, 0, 0, 0, algorithm, "Error");
        }
    }

    /// <summary>
    /// Maps cycle count to ZOSAPI OptimizationCycles enum
    /// </summary>
    public static OptimizationCycles MapCyclesToEnum(int cycles)
    {
        return cycles switch
        {
            0 => OptimizationCycles.Automatic,
            1 => OptimizationCycles.Fixed_1_Cycle,
            <= 5 => OptimizationCycles.Fixed_5_Cycles,
            <= 10 => OptimizationCycles.Fixed_10_Cycles,
            <= 50 => OptimizationCycles.Fixed_50_Cycles,
            _ => OptimizationCycles.Infinite // For large cycle counts, use infinite and let it run
        };
    }

    /// <summary>
    /// Gets expected cycle count for reporting
    /// </summary>
    private static int GetExpectedCycleCount(int requestedCycles)
    {
        return requestedCycles switch
        {
            0 => -1, // Automatic - unknown
            1 => 1,
            <= 5 => 5,
            <= 10 => 10,
            <= 50 => 50,
            _ => requestedCycles
        };
    }
}
