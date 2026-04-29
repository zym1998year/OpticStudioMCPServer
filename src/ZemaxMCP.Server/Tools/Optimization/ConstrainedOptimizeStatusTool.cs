using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeStatusTool
{
    private readonly ConstrainedOptimizeState _state;
    public ConstrainedOptimizeStatusTool(ConstrainedOptimizeState state) => _state = state;

    public record ConstrainedOptimizeStatusResult(
        bool IsRunning,
        int Iteration,
        int MaxIterations,
        double InitialMerit,
        double CurrentMerit,
        double Mu,
        int RestartsUsed,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_constrained_optimize_status")]
    [Description("Check progress of a running zemax_constrained_optimize_async. Non-blocking.")]
    public ConstrainedOptimizeStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Constrained Optimize running: iter {_state.Iteration}/{_state.MaxIterations}, " +
                      $"merit {_state.CurrentMerit:F6}, mu {_state.Mu:F4}, " +
                      $"restarts {_state.RestartsUsed}, runtime {_state.RuntimeSeconds:F0}s";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Constrained Optimize {_state.TerminationReason}{errPart}: " +
                      $"final merit {_state.CurrentMerit:F6}, iter {_state.Iteration}, " +
                      $"runtime {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Constrained Optimize has been run yet.";
        }

        return new ConstrainedOptimizeStatusResult(
            IsRunning: _state.IsRunning,
            Iteration: _state.Iteration,
            MaxIterations: _state.MaxIterations,
            InitialMerit: _state.InitialMerit,
            CurrentMerit: _state.CurrentMerit,
            Mu: _state.Mu,
            RestartsUsed: _state.RestartsUsed,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
