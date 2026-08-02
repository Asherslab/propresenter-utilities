using ProPresenter.Api;

namespace ProPresenter.Triggers.Tests;

/// <summary>
/// Regression cover for the connect handler. Reconnecting must discard timer baselines so that
/// already-passed cues are not fired late — but treating an ordinary tick as a reconnection discards
/// the baseline every second, which stops every cue from ever firing.
/// </summary>
public class ConnectionLifecycleTests
{
    [Fact]
    public void First_connection_resets_baselines()
    {
        Assert.True(TriggerEngine.IsNewConnection(null, ConnectionState.Connected));
    }

    [Fact]
    public void Reconnecting_after_a_drop_resets_baselines()
    {
        Assert.True(TriggerEngine.IsNewConnection(ConnectionState.Disconnected, ConnectionState.Connected));
        Assert.True(TriggerEngine.IsNewConnection(ConnectionState.Connecting, ConnectionState.Connected));
    }

    [Fact]
    public void A_repeated_connected_notification_does_not_reset_baselines()
    {
        Assert.False(TriggerEngine.IsNewConnection(ConnectionState.Connected, ConnectionState.Connected));
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.Connecting)]
    public void Non_connected_states_never_reset_baselines(ConnectionState state)
    {
        Assert.False(TriggerEngine.IsNewConnection(ConnectionState.Connected, state));
        Assert.False(TriggerEngine.IsNewConnection(null, state));
    }
}
