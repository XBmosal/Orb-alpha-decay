using FlowTerminal.Rithmic;
using Xunit;

namespace FlowTerminal.MarketData.Tests;

public class RithmicLoginTests
{
    private static RithmicCredentials Complete() => new()
    {
        Username = "trader1",
        Password = "s3cr3t-pw",
        SystemName = "Rithmic Test",
        Gateway = "Chicago Area",
        Environment = RithmicEnvironment.Test,
    };

    [Fact]
    public void IsComplete_Requires_Username_Password_And_System()
    {
        Assert.True(Complete().IsComplete);
        Assert.False((Complete() with { Username = "" }).IsComplete);
        Assert.False((Complete() with { Password = "" }).IsComplete);
        Assert.False((Complete() with { SystemName = "" }).IsComplete);
        Assert.True((Complete() with { Gateway = "" }).IsComplete); // gateway is optional
    }

    [Fact]
    public void ToString_Never_Leaks_The_Password()
    {
        var creds = Complete();
        string s = creds.ToString();
        Assert.DoesNotContain("s3cr3t-pw", s);
        Assert.Contains("***", s);
        Assert.Contains("trader1", s);   // non-secret fields are fine to show

        // Interpolation and record formatting both route through the redacting ToString.
        Assert.DoesNotContain("s3cr3t-pw", $"{creds}");
        Assert.DoesNotContain("s3cr3t-pw", $"login failed for {creds}");
    }

    [Fact]
    public async Task Connect_Reports_SdkUnavailable_In_Mock_Build()
    {
        // In the normal (non-RITHMIC_SDK) build there is no live connection; the session
        // must say so honestly rather than fake a connected state.
        var result = await new RithmicSession().ConnectAsync(Complete());

        if (RithmicAvailability.IsCompiledIn)
        {
            // With the SDK compiled in this reaches the (unimplemented) adapter region.
            Assert.NotEqual(RithmicConnectionOutcome.InvalidCredentials, result.Outcome);
        }
        else
        {
            Assert.Equal(RithmicConnectionOutcome.SdkUnavailable, result.Outcome);
            Assert.False(result.IsConnected);
            Assert.DoesNotContain("s3cr3t-pw", result.Message);
        }
    }

    [Fact]
    public async Task Connect_Rejects_Incomplete_Credentials()
    {
        var result = await new RithmicSession().ConnectAsync(Complete() with { Password = "" });
        Assert.Equal(RithmicConnectionOutcome.InvalidCredentials, result.Outcome);
        Assert.False(result.IsConnected);
    }
}
