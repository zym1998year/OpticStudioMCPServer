using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeStopTool
{
    private readonly OptimizeState _state;
    public OptimizeStopTool(OptimizeState state) => _state = state;

    public record OptimizeStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_optimize_stop")]
    [Description("Cancel the running zemax_optimize_async optimization. Re-poll zemax_optimize_status for final state.")]
    public OptimizeStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new OptimizeStopResult(false, "No running Optimize to stop.");
        }
        _state.RequestCancellation();
        return new OptimizeStopResult(true,
            "Cancellation requested. Re-poll zemax_optimize_status for final state.");
    }
}
