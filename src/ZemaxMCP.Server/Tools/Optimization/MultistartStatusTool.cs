using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class MultistartStatusTool
{
    private readonly MultistartState _state;

    public MultistartStatusTool(MultistartState state)
    {
        _state = state;
    }

    public record MultistartStatusResult(
        bool IsRunning,
        bool IsInInitialLm,
        int CurrentTrial,
        int MaxTrials,
        double InitialMerit,
        double BestMerit,
        int TotalTrialsRun,
        int TotalTrialsAccepted,
        int SaveCount,
        string? SaveFolder,
        string? ErrorMessage,
        int InitialLmIteration,
        int InitialLmMaxIterations,
        double InitialLmMerit,
        string Summary
    );

    [McpServerTool(Name = "zemax_multistart_status")]
    [Description("Check the progress of a running multistart optimization. Returns current trial, best merit, acceptance count, and running state. Does not block — safe to call while optimization is running.")]
    public MultistartStatusResult Execute()
    {
        string summary;

        if (_state.IsRunning)
        {
            if (_state.IsInInitialLm)
            {
                if (_state.InitialLmMaxIterations > 0)
                {
                    double pct = (double)_state.InitialLmIteration / _state.InitialLmMaxIterations * 100;
                    summary = $"Initial LM optimization: iteration {_state.InitialLmIteration}/{_state.InitialLmMaxIterations} ({pct:F0}%) | Merit: {_state.InitialLmMerit:F6}";
                }
                else
                {
                    summary = "Starting initial LM optimization...";
                }
            }
            else
            {
                double pct = _state.MaxTrials > 0 ? (double)_state.CurrentTrial / _state.MaxTrials * 100 : 0;
                summary = $"Trial {_state.CurrentTrial}/{_state.MaxTrials} ({pct:F1}%) | " +
                          $"Best merit: {_state.BestMerit:F6} | " +
                          $"Accepted: {_state.TotalTrialsAccepted} | " +
                          $"Saves: {_state.SaveCount}";
            }
        }
        else if (_state.HasState)
        {
            var errorPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Completed{errorPart}. Final merit: {_state.BestMerit:F6} | " +
                      $"Trials: {_state.TotalTrialsRun} | Accepted: {_state.TotalTrialsAccepted} | " +
                      $"Saves: {_state.SaveCount}";
        }
        else
        {
            summary = "No multistart optimization has been run yet.";
        }

        return new MultistartStatusResult(
            IsRunning: _state.IsRunning,
            IsInInitialLm: _state.IsInInitialLm,
            CurrentTrial: _state.CurrentTrial,
            MaxTrials: _state.MaxTrials,
            InitialMerit: _state.InitialMerit,
            BestMerit: _state.BestMerit,
            TotalTrialsRun: _state.TotalTrialsRun,
            TotalTrialsAccepted: _state.TotalTrialsAccepted,
            SaveCount: _state.SaveCount,
            SaveFolder: _state.SaveFolder,
            ErrorMessage: _state.ErrorMessage,
            InitialLmIteration: _state.InitialLmIteration,
            InitialLmMaxIterations: _state.InitialLmMaxIterations,
            InitialLmMerit: _state.InitialLmMerit,
            Summary: summary
        );
    }
}
