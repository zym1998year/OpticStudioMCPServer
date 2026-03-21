using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class MultistartStopTool
{
    private readonly MultistartState _state;

    public MultistartStopTool(MultistartState state)
    {
        _state = state;
    }

    public record MultistartStopResult(
        bool Success,
        string Message
    );

    [McpServerTool(Name = "zemax_multistart_stop")]
    [Description("Stop a running multistart optimization. The optimizer will finish its current trial, restore the best state found so far, and stop. Use zemax_multistart_status to confirm it has stopped.")]
    public MultistartStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new MultistartStopResult(false,
                "No multistart optimization is currently running.");
        }

        _state.RequestCancellation();

        return new MultistartStopResult(true,
            $"Cancellation requested. The optimizer will stop after the current trial " +
            $"(currently on trial {_state.CurrentTrial}/{_state.MaxTrials}). " +
            $"Best merit so far: {_state.BestMerit:F6}. " +
            "Use zemax_multistart_status to confirm completion.");
    }
}
