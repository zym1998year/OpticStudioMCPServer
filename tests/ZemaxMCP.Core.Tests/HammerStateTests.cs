using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Core.Tests;

public class HammerStateTests
{
    [Fact]
    public void NewInstance_HasCleanState()
    {
        var state = new HammerState();

        Assert.False(state.IsRunning);
        Assert.Null(state.Algorithm);
        Assert.Equal(0, state.Improvements);
        Assert.Null(state.TerminationReason);
        Assert.Null(state.ErrorMessage);
        Assert.Equal(0.0, state.InitialMerit);
        Assert.Equal(0.0, state.CurrentMerit);
        Assert.Equal(0.0, state.BestMerit);
        Assert.Equal(0.0, state.RuntimeSeconds);
        Assert.False(state.HasState);
    }

    [Fact]
    public void SetRunning_FlipsFlagAndCapturesInitialFields()
    {
        var state = new HammerState();

        state.SetRunning("DLS", 12.5);

        Assert.True(state.IsRunning);
        Assert.Equal("DLS", state.Algorithm);
        Assert.Equal(12.5, state.InitialMerit);
        Assert.Equal(12.5, state.CurrentMerit);
        Assert.Equal(12.5, state.BestMerit);
        Assert.Equal(0, state.Improvements);
        Assert.Null(state.TerminationReason);
        Assert.Null(state.ErrorMessage);
        Assert.False(state.HasState);  // running, not terminal
    }

    [Fact]
    public void UpdateMerit_DoesNotFlipIsRunning()
    {
        var state = new HammerState();
        state.SetRunning("DLS", 100.0);

        state.UpdateMerit(currentMerit: 80.0, bestMerit: 75.0,
                          runtimeSeconds: 5.5, improvements: 3);

        Assert.True(state.IsRunning);
        Assert.Equal(80.0, state.CurrentMerit);
        Assert.Equal(75.0, state.BestMerit);
        Assert.Equal(5.5, state.RuntimeSeconds);
        Assert.Equal(3, state.Improvements);
        Assert.Equal(100.0, state.InitialMerit);  // unchanged
    }

    [Fact]
    public void SetCompleted_FlipsToTerminalState()
    {
        var state = new HammerState();
        state.SetRunning("DLS", 100.0);
        state.UpdateMerit(80.0, 75.0, 10.0, 2);

        state.SetCompleted("MaxRuntime");

        Assert.False(state.IsRunning);
        Assert.Equal("MaxRuntime", state.TerminationReason);
        Assert.Null(state.ErrorMessage);
        Assert.True(state.HasState);
        // Last-known merit fields preserved:
        Assert.Equal(75.0, state.BestMerit);
        Assert.Equal(2, state.Improvements);
    }

    [Fact]
    public void SetCompleted_WithError_CapturesErrorMessage()
    {
        var state = new HammerState();
        state.SetRunning("DLS", 100.0);

        state.SetCompleted("Error", "Zemax COM threw");

        Assert.False(state.IsRunning);
        Assert.Equal("Error", state.TerminationReason);
        Assert.Equal("Zemax COM threw", state.ErrorMessage);
        Assert.True(state.HasState);
    }

    [Fact]
    public void Reset_ClearsAllFieldsAndHasStateGoesFalse()
    {
        var state = new HammerState();
        state.SetRunning("DLS", 100.0);
        state.UpdateMerit(80.0, 75.0, 10.0, 2);
        state.SetCompleted("MaxRuntime");
        Assert.True(state.HasState);

        state.Reset();

        Assert.False(state.IsRunning);
        Assert.Null(state.Algorithm);
        Assert.Equal(0, state.Improvements);
        Assert.Null(state.TerminationReason);
        Assert.Null(state.ErrorMessage);
        Assert.Equal(0.0, state.InitialMerit);
        Assert.Equal(0.0, state.CurrentMerit);
        Assert.Equal(0.0, state.BestMerit);
        Assert.Equal(0.0, state.RuntimeSeconds);
        Assert.False(state.HasState);
    }

    [Fact]
    public void CreateCancellationToken_ReturnsValidToken()
    {
        var state = new HammerState();

        var ct = state.CreateCancellationToken();

        Assert.False(ct.IsCancellationRequested);
        Assert.True(ct.CanBeCanceled);
    }

    [Fact]
    public void RequestCancellation_FiresTokenCancellation()
    {
        var state = new HammerState();
        var ct = state.CreateCancellationToken();
        Assert.False(ct.IsCancellationRequested);

        state.RequestCancellation();

        Assert.True(ct.IsCancellationRequested);
    }

    [Fact]
    public void CreateCancellationToken_TwiceDisposesPreviousAndReturnsFreshToken()
    {
        var state = new HammerState();
        var ct1 = state.CreateCancellationToken();

        var ct2 = state.CreateCancellationToken();

        // ct1's underlying source has been disposed; ct1 still readable but the
        // canonical token is now ct2. We don't assert ct1 disposal because the
        // struct snapshot keeps the cancel state queryable.
        Assert.False(ct2.IsCancellationRequested);
        Assert.True(ct2.CanBeCanceled);

        // Cancel via state -- only ct2 should observe.
        state.RequestCancellation();
        Assert.True(ct2.IsCancellationRequested);
    }

    [Fact]
    public void RequestCancellation_WithoutPriorCreate_IsNoOp()
    {
        var state = new HammerState();

        // Should not throw.
        state.RequestCancellation();

        Assert.False(state.IsRunning);
    }

    [Fact]
    public void SetBackgroundTask_StoresAndReturnsReference()
    {
        var state = new HammerState();
        var task = Task.CompletedTask;

        state.SetBackgroundTask(task);

        Assert.Same(task, state.BackgroundTask);
    }

    [Fact]
    public void HasState_TrueOnlyAfterCompleted()
    {
        var state = new HammerState();
        Assert.False(state.HasState);

        state.SetRunning("DLS", 1.0);
        Assert.False(state.HasState);  // running

        state.UpdateMerit(0.5, 0.5, 1.0, 1);
        Assert.False(state.HasState);  // still running

        state.SetCompleted("Stagnation");
        Assert.True(state.HasState);

        state.Reset();
        Assert.False(state.HasState);
    }
}
