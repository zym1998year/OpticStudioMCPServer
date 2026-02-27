using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class HammerOptimizationTool
{
    private readonly IZemaxSession _session;

    public HammerOptimizationTool(IZemaxSession session) => _session = session;

    public record HammerResult(
        bool Success,
        string? Error,
        double InitialMerit,
        double FinalMerit,
        double Improvement,
        string Algorithm,
        double RuntimeSeconds,
        int Variables,
        int Improvements,
        string TerminationReason
    );

    [McpServerTool(Name = "zemax_hammer")]
    [Description("Run Hammer optimization on the current optical system. Hammer continuously optimizes and supports glass substitution when surfaces have MaterialSubstitute solve set.")]
    public async Task<HammerResult> ExecuteAsync(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Target runtime in minutes (for automatic mode)")] double targetRuntimeMinutes = 1.0,
        [Description("Maximum runtime in seconds (timeout)")] double timeoutSeconds = 120,
        [Description("Use automatic optimization mode (true) or fixed cycles (false)")] bool automatic = true)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["algorithm"] = algorithm,
                ["cores"] = cores,
                ["targetRuntimeMinutes"] = targetRuntimeMinutes,
                ["timeoutSeconds"] = timeoutSeconds,
                ["automatic"] = automatic
            };

            var result = await _session.ExecuteAsync("Hammer", parameters, system =>
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

                var hammer = system.Tools?.OpenHammerOptimization();
                if (hammer == null)
                {
                    throw new InvalidOperationException("Failed to open Hammer Optimization tool");
                }

                try
                {
                    // Set algorithm
                    hammer.Algorithm = algorithm.ToUpper() switch
                    {
                        "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                        "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                        _ => OptimizationAlgorithm.DampedLeastSquares
                    };

                    // Set number of cores (0 means use MaxCores)
                    if (cores > 0)
                    {
                        hammer.NumberOfCores = Math.Min(cores, hammer.MaxCores);
                    }
                    else
                    {
                        hammer.NumberOfCores = hammer.MaxCores;
                    }

                    // Get variable count
                    var variables = hammer.Variables;

                    string terminationReason;
                    var stopwatch = Stopwatch.StartNew();

                    // Start Hammer with non-blocking Run(), then poll and cancel
                    hammer.Run();

                    double bestMerit = double.MaxValue;
                    long lastImprovedMs = 0;
                    long timeoutMs = (long)(timeoutSeconds * 1000);
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

                            long idleMs = now - lastImprovedMs;

                            // Stop if stagnated (no improvement for timeoutSeconds)
                            if (idleMs >= timeoutMs)
                                break;
                        }
                        catch
                        {
                            // Hammer may throw while running; ignore and keep polling
                        }
                    }

                    hammer.Cancel();
                    hammer.WaitForCompletion();

                    stopwatch.Stop();
                    var finalMerit = hammer.CurrentMeritFunction;
                    terminationReason = improvements > 0 ? "Stagnation" : "NoImprovement";

                    return new HammerResult(
                        Success: true,
                        Error: null,
                        InitialMerit: initialMerit,
                        FinalMerit: finalMerit,
                        Improvement: initialMerit - finalMerit,
                        Algorithm: algorithm,
                        RuntimeSeconds: stopwatch.Elapsed.TotalSeconds,
                        Variables: variables,
                        Improvements: improvements,
                        TerminationReason: terminationReason
                    );
                }
                finally
                {
                    hammer.Close();
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            return new HammerResult(false, ex.Message, 0, 0, 0, algorithm, 0, 0, 0, "Error");
        }
    }
}
