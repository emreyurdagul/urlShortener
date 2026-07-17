namespace Gateway.Tests;

public class BackendTests
{
    [Fact]
    public void Starts_healthy()
    {
        Assert.True(new Backend("a").IsHealthy);
    }

    [Fact]
    public void SetHealthy_reports_only_actual_transitions()
    {
        var backend = new Backend("a");

        Assert.False(backend.SetHealthy(true));   // healthy -> healthy: no change
        Assert.True(backend.SetHealthy(false));   // healthy -> down
        Assert.False(backend.SetHealthy(false));  // down -> down: no change
        Assert.True(backend.SetHealthy(true));    // down -> healthy
        Assert.True(backend.IsHealthy);
    }
}
