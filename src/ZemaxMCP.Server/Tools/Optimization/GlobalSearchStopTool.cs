using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchStopTool
{
    private readonly GlobalSearchState _state;
    public GlobalSearchStopTool(GlobalSearchState state) => _state = state;

    public record GlobalSearchStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_global_search_stop")]
    [Description(@"Cancel the running zemax_global_search_async optimization. The
GlobalOptimization.Cancel() will be issued. Re-poll zemax_global_search_status
to observe the final ""Cancelled"" state.")]
    public GlobalSearchStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new GlobalSearchStopResult(false, "No running Global Search to stop.");
        }
        _state.RequestCancellation();
        return new GlobalSearchStopResult(true,
            "Cancellation requested. Re-poll zemax_global_search_status for final state.");
    }
}
