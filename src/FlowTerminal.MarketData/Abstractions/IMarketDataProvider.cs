using FlowTerminal.Domain.Capabilities;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.MarketData.Abstractions;

/// <summary>
/// A live (or live-like) source of canonical <see cref="MarketEvent"/>s for a
/// contract. Mock, replay, and the authorized Rithmic adapter all implement this
/// identical interface, so every downstream consumer is source-agnostic.
///
/// This is strictly a DATA provider. There is intentionally no execution surface:
/// no order submission, modification, cancellation, account, or position concept.
/// </summary>
public interface IMarketDataProvider : IAsyncDisposable
{
    SourceProvider Source { get; }

    ProviderCapabilities Capabilities { get; }

    ConnectionState ConnectionState { get; }

    event Action<ConnectionState>? ConnectionStateChanged;

    Task ConnectAsync(CancellationToken cancellationToken);

    Task SubscribeAsync(Contract contract, SubscriptionOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// The canonical event stream. Consumers pull at their own pace; the pipeline
    /// applies bounded backpressure. Canonical events are never silently dropped.
    /// </summary>
    IAsyncEnumerable<MarketEvent> StreamAsync(CancellationToken cancellationToken);

    Task DisconnectAsync();
}
