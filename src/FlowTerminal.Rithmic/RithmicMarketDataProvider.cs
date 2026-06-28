using FlowTerminal.Domain.Capabilities;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.Rithmic;

/// <summary>
/// The authorized Rithmic market-data adapter boundary. It implements the same
/// <see cref="IMarketDataProvider"/> contract as the mock/replay providers, so the
/// rest of the application is unaware of the data source.
///
/// IMPORTANT — no SDK behavior is fabricated here. The official Rithmic R|API+ .NET
/// SDK is proprietary and is not part of this repository. When the authorized SDK
/// is installed and the <c>RITHMIC_SDK</c> build symbol is defined, the real
/// connection/authentication/subscription logic is wired in the marked region
/// below against the SDK's documented types. Until then every operation fails
/// loudly and honestly via <see cref="RithmicSdkUnavailableException"/> — the app
/// stays fully buildable and mock mode is unaffected.
/// </summary>
public sealed class RithmicMarketDataProvider : IMarketDataProvider
{
    public SourceProvider Source => SourceProvider.Rithmic;

    /// <summary>
    /// Capabilities are reported as <see cref="DataCapabilities.None"/> until a real
    /// connection is established and capability detection runs against the entitled
    /// feed. We never claim a capability we have not confirmed.
    /// </summary>
    public ProviderCapabilities Capabilities { get; } = new("Rithmic (not connected)", DataCapabilities.None);

    public ConnectionState ConnectionState => ConnectionState.Disconnected;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        // Keep the interface event referenced under every build profile. Real state
        // transitions are raised from the SDK region once the adapter is wired in.
        _ = ConnectionStateChanged;
#if RITHMIC_SDK
        // ──────────────────────────────────────────────────────────────────────
        // Authorized Rithmic R|API+ .NET integration goes here, written against the
        // SDK's OWN documented types, events, and callbacks. Do not invent symbols.
        // Required steps per the official SDK documentation:
        //   1. Engine/params init (environment, system, gateway selection)
        //   2. Login / authentication using securely stored credentials
        //   3. Connection-state + heartbeat monitoring
        // This region is intentionally left unimplemented in source control because
        // the proprietary SDK is required to compile it correctly.
        // ──────────────────────────────────────────────────────────────────────
        throw new NotImplementedException(
            "Wire the authorized Rithmic R|API+ SDK here. See lib/rithmic/README.md.");
#else
        throw new RithmicSdkUnavailableException();
#endif
    }

    public Task SubscribeAsync(Contract contract, SubscriptionOptions options, CancellationToken cancellationToken)
        => throw new RithmicSdkUnavailableException();

    public IAsyncEnumerable<MarketEvent> StreamAsync(CancellationToken cancellationToken)
        => throw new RithmicSdkUnavailableException();

    public Task DisconnectAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
