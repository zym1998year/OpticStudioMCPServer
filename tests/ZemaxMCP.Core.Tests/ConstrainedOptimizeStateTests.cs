using Xunit;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Core.Tests;

public class ConstrainedOptimizeStateTests
{
    [Fact]
    public void SetRunning_NoAlgorithmField_CapturesLmFields()
    {
        var state = new ConstrainedOptimizeState();

        state.SetRunning(maxIterations: 200, initialMu: 1e-3, initialMerit: 5.0);

        Assert.True(state.IsRunning);
        Assert.Equal(200, state.MaxIterations);
        Assert.Equal(0, state.Iteration);
        Assert.Equal(0, state.RestartsUsed);
        Assert.Equal(5.0, state.InitialMerit);
        Assert.Equal(5.0, state.CurrentMerit);
        Assert.Equal(1e-3, state.Mu);
    }

    [Fact]
    public void UpdateProgress_TracksLmFields()
    {
        var state = new ConstrainedOptimizeState();
        state.SetRunning(200, 1e-3, 5.0);

        state.UpdateProgress(iteration: 35, currentMerit: 2.1, mu: 5e-4,
                             runtimeSeconds: 12.0, restartsUsed: 1);

        Assert.Equal(35, state.Iteration);
        Assert.Equal(2.1, state.CurrentMerit);
        Assert.Equal(5e-4, state.Mu);
        Assert.Equal(12.0, state.RuntimeSeconds);
        Assert.Equal(1, state.RestartsUsed);
    }

    [Fact]
    public void Reset_ClearsLmFields()
    {
        var state = new ConstrainedOptimizeState();
        state.SetRunning(100, 1e-3, 5.0);
        state.UpdateProgress(20, 1.5, 1e-4, 8.0, 2);
        state.SetCompleted("Cancelled");

        state.Reset();

        Assert.False(state.IsRunning);
        Assert.Equal(0, state.Iteration);
        Assert.Equal(0, state.MaxIterations);
        Assert.Equal(0, state.RestartsUsed);
        Assert.Equal(0.0, state.Mu);
        Assert.Null(state.TerminationReason);
        Assert.False(state.HasState);
    }
}
