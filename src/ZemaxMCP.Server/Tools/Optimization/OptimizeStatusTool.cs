using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeStatusTool
{
    private readonly OptimizeState _state;
    public OptimizeStatusTool(OptimizeState state) => _state = state;

    public record OptimizeStatusResult(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double CurrentMerit,
        int CyclesCompleted,
        int CyclesRequested,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_optimize_status")]
    [Description("Check the progress of a running zemax_optimize_async optimization. Non-blocking.")]
    public OptimizeStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Optimize running ({_state.Algorithm}): cycle {_state.CyclesCompleted}" +
                      (_state.CyclesRequested > 0 ? $"/{_state.CyclesRequested}" : "/auto") +
                      $", merit {_state.CurrentMerit:F6}, runtime {_state.RuntimeSeconds:F0}s";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Optimize {_state.TerminationReason}{errPart}: " +
                      $"final merit {_state.CurrentMerit:F6}, cycles {_state.CyclesCompleted}, " +
                      $"runtime {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Optimize has been run yet.";
        }

        return new OptimizeStatusResult(
            IsRunning: _state.IsRunning,
            Algorithm: _state.Algorithm,
            InitialMerit: _state.InitialMerit,
            CurrentMerit: _state.CurrentMerit,
            CyclesCompleted: _state.CyclesCompleted,
            CyclesRequested: _state.CyclesRequested,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
