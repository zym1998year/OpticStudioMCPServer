using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class HammerStatusTool
{
    private readonly HammerState _state;
    public HammerStatusTool(HammerState state) => _state = state;

    public record HammerStatusResult(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double CurrentMerit,
        double BestMerit,
        int Improvements,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_hammer_status")]
    [Description("Check the progress of a running zemax_hammer_async optimization. Non-blocking; safe to call any time.")]
    public HammerStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Hammer running ({_state.Algorithm}): {_state.RuntimeSeconds:F0}s elapsed, " +
                      $"best merit {_state.BestMerit:F6}, improvements: {_state.Improvements}";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Hammer {_state.TerminationReason}{errPart}: " +
                      $"final merit {_state.BestMerit:F6}, improvements: {_state.Improvements}, " +
                      $"runtime: {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Hammer optimization has been run yet.";
        }

        return new HammerStatusResult(
            IsRunning: _state.IsRunning,
            Algorithm: _state.Algorithm,
            InitialMerit: _state.InitialMerit,
            CurrentMerit: _state.CurrentMerit,
            BestMerit: _state.BestMerit,
            Improvements: _state.Improvements,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
