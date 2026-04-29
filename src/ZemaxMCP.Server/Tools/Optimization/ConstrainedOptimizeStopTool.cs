using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeStopTool
{
    private readonly ConstrainedOptimizeState _state;
    public ConstrainedOptimizeStopTool(ConstrainedOptimizeState state) => _state = state;

    public record ConstrainedOptimizeStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_constrained_optimize_stop")]
    [Description("Cancel the running zemax_constrained_optimize_async. Re-poll status for final state.")]
    public ConstrainedOptimizeStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new ConstrainedOptimizeStopResult(false, "No running Constrained Optimize to stop.");
        }
        _state.RequestCancellation();
        return new ConstrainedOptimizeStopResult(true,
            "Cancellation requested. Re-poll zemax_constrained_optimize_status for final state.");
    }
}
