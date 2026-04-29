using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchStatusTool
{
    private readonly GlobalSearchState _state;
    public GlobalSearchStatusTool(GlobalSearchState state) => _state = state;

    public record GlobalSearchStatusResult(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double BestMerit,
        int SolutionsValid,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_global_search_status")]
    [Description("Check the progress of a running zemax_global_search_async optimization. Non-blocking.")]
    public GlobalSearchStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Global Search running ({_state.Algorithm}): {_state.RuntimeSeconds:F0}s elapsed, " +
                      $"best merit {_state.BestMerit:F6}, valid solutions: {_state.SolutionsValid}";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Global Search {_state.TerminationReason}{errPart}: " +
                      $"final best {_state.BestMerit:F6}, " +
                      $"solutions: {_state.SolutionsValid}, runtime: {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Global Search has been run yet.";
        }

        return new GlobalSearchStatusResult(
            IsRunning: _state.IsRunning,
            Algorithm: _state.Algorithm,
            InitialMerit: _state.InitialMerit,
            BestMerit: _state.BestMerit,
            SolutionsValid: _state.SolutionsValid,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
