namespace FlowTerminal.Rithmic;

/// <summary>
/// Coordinates a Rithmic sign-in attempt. It validates the credentials, then either
/// connects through the authorized SDK adapter (when the <c>RITHMIC_SDK</c> build is
/// active) or reports honestly that the SDK is not present and the app is running in
/// mock/replay mode. It never fabricates a "connected" state, and never logs the password.
/// </summary>
public sealed class RithmicSession
{
    private RithmicMarketDataProvider? _provider;

    /// <summary>True once a real Rithmic connection is live (only possible with the SDK build).</summary>
    public bool IsConnected { get; private set; }

    /// <summary>The provider to feed live data from, or null when not connected.</summary>
    public RithmicMarketDataProvider? Provider => IsConnected ? _provider : null;

    public async Task<RithmicConnectionResult> ConnectAsync(RithmicCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (!credentials.IsComplete)
        {
            return new RithmicConnectionResult(
                RithmicConnectionOutcome.InvalidCredentials,
                "Enter a username, password and system name to connect.");
        }

        if (!RithmicAvailability.IsCompiledIn)
        {
            // Honest state: no proprietary SDK in this build, so no live connection is
            // possible. The app continues on mock/replay data.
            return new RithmicConnectionResult(
                RithmicConnectionOutcome.SdkUnavailable,
                "Rithmic SDK is not installed in this build — staying on mock/replay data. " +
                "Install the authorized SDK and enable the Rithmic build profile (lib/rithmic/README.md).");
        }

        var provider = new RithmicMarketDataProvider();
        provider.Configure(credentials);
        try
        {
            await provider.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _provider = provider;
            IsConnected = true;
            return new RithmicConnectionResult(
                RithmicConnectionOutcome.Connected,
                $"Connected to {credentials.SystemName} ({credentials.Environment}).");
        }
        catch (RithmicSdkUnavailableException ex)
        {
            return new RithmicConnectionResult(RithmicConnectionOutcome.SdkUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            // The message comes from the SDK boundary and never contains the password.
            return new RithmicConnectionResult(RithmicConnectionOutcome.Failed, $"Rithmic login failed: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisconnectAsync().ConfigureAwait(false);
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
        _provider = null;
        IsConnected = false;
    }
}
