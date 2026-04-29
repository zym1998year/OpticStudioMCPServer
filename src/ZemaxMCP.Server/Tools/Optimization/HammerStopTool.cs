using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class HammerStopTool
{
    private readonly HammerState _state;
    public HammerStopTool(HammerState state) => _state = state;

    public record HammerStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_hammer_stop")]
    [Description(@"Cancel the running zemax_hammer_async optimization. The Hammer.Cancel()
will be issued and the runner finalises shortly after. Status often reads
""running"" briefly post-cancel — re-poll zemax_hammer_status to observe the
final ""Cancelled"" state.")]
    public HammerStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new HammerStopResult(false, "No running Hammer optimization to stop.");
        }
        _state.RequestCancellation();
        return new HammerStopResult(true,
            "Cancellation requested. Re-poll zemax_hammer_status for final state.");
    }
}
