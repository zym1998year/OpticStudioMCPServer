using System.Threading.Tasks;
using Xunit;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Core.Tests;

public class GlobalSearchStateTests
{
    [Fact]
    public void NewInstance_HasCleanState()
    {
        var state = new GlobalSearchState();

        Assert.False(state.IsRunning);
        Assert.Null(state.Algorithm);
        Assert.Equal(0, state.SolutionsValid);
        Assert.False(state.HasState);
    }

    [Fact]
    public void SetRunning_InitializesFields()
    {
        var state = new GlobalSearchState();
        state.SetRunning("Orthogonal", 50.0);

        Assert.True(state.IsRunning);
        Assert.Equal("Orthogonal", state.Algorithm);
        Assert.Equal(50.0, state.InitialMerit);
        Assert.Equal(50.0, state.BestMerit);
    }

    [Fact]
    public void UpdateProgress_TracksSolutionsValid()
    {
        var state = new GlobalSearchState();
        state.SetRunning("DLS", 100.0);

        state.UpdateProgress(bestMerit: 25.0, runtimeSeconds: 30.0, solutionsValid: 7);

        Assert.Equal(25.0, state.BestMerit);
        Assert.Equal(7, state.SolutionsValid);
        Assert.Equal(30.0, state.RuntimeSeconds);
    }

    [Fact]
    public void Reset_ClearsAllFields()
    {
        var state = new GlobalSearchState();
        state.SetRunning("DLS", 100.0);
        state.UpdateProgress(50.0, 10.0, 3);
        state.SetCompleted("Timeout");

        state.Reset();

        Assert.False(state.IsRunning);
        Assert.Null(state.Algorithm);
        Assert.Equal(0, state.SolutionsValid);
        Assert.Null(state.TerminationReason);
        Assert.Equal(0.0, state.BestMerit);
        Assert.False(state.HasState);
    }

    [Fact]
    public void SetCompleted_WithError_RetainsLastBestMerit()
    {
        var state = new GlobalSearchState();
        state.SetRunning("DLS", 100.0);
        state.UpdateProgress(45.0, 20.0, 5);

        state.SetCompleted("Error", "ZOSAPI threw");

        Assert.False(state.IsRunning);
        Assert.Equal("Error", state.TerminationReason);
        Assert.Equal("ZOSAPI threw", state.ErrorMessage);
        Assert.Equal(45.0, state.BestMerit);
        Assert.Equal(5, state.SolutionsValid);
        Assert.True(state.HasState);
    }
}
