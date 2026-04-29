using Xunit;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Core.Tests;

public class OptimizeStateTests
{
    [Fact]
    public void SetRunning_CapturesCyclesRequested()
    {
        var state = new OptimizeState();

        state.SetRunning("DLS", cyclesRequested: 50, initialMerit: 100.0);

        Assert.True(state.IsRunning);
        Assert.Equal("DLS", state.Algorithm);
        Assert.Equal(50, state.CyclesRequested);
        Assert.Equal(0, state.CyclesCompleted);
        Assert.Equal(100.0, state.InitialMerit);
        Assert.Equal(100.0, state.CurrentMerit);
    }

    [Fact]
    public void UpdateProgress_TracksCyclesCompleted()
    {
        var state = new OptimizeState();
        state.SetRunning("DLS", 50, 100.0);

        state.UpdateProgress(cyclesCompleted: 12, currentMerit: 60.0,
                             runtimeSeconds: 8.0);

        Assert.Equal(12, state.CyclesCompleted);
        Assert.Equal(60.0, state.CurrentMerit);
        Assert.Equal(8.0, state.RuntimeSeconds);
        Assert.Equal(50, state.CyclesRequested);  // unchanged
    }

    [Fact]
    public void Reset_ClearsAllFields()
    {
        var state = new OptimizeState();
        state.SetRunning("DLS", 50, 100.0);
        state.UpdateProgress(10, 80.0, 5.0);
        state.SetCompleted("Completed");
        Assert.True(state.HasState);

        state.Reset();

        Assert.False(state.IsRunning);
        Assert.Null(state.Algorithm);
        Assert.Equal(0, state.CyclesCompleted);
        Assert.Equal(0, state.CyclesRequested);
        Assert.Null(state.TerminationReason);
        Assert.Equal(0.0, state.CurrentMerit);
        Assert.False(state.HasState);
    }
}
