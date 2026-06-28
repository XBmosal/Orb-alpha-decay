using System.Runtime.CompilerServices;
using FlowTerminal.Domain.Capabilities;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Domain.Time;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.MarketData.Synthetic;

/// <summary>
/// A fully functional, Rithmic-free market-data provider backed by the
/// deterministic <see cref="SyntheticSessionGenerator"/>. It exercises the same
/// canonical event pipeline as the live adapter, so mock mode is a first-class,
/// always-available run mode (used by CI, demos, and tests).
///
/// Capabilities advertised match what a synthetic feed can honestly provide:
/// trades, top of book, MBP depth, exchange timestamps, sequence numbers, and a
/// supplied aggressor side. It does not claim MBO or historical depth it lacks.
/// </summary>
public sealed class MockMarketDataProvider : IMarketDataProvider
{
    private readonly int _instrumentId;
    private readonly Contract _contract;
    private readonly SyntheticOptions _options;
    private readonly IClock _clock;
    private readonly bool _realTimePacing;
    private ConnectionState _state = ConnectionState.Disconnected;

    public MockMarketDataProvider(
        int instrumentId,
        Contract contract,
        SyntheticOptions? options = null,
        IClock? clock = null,
        bool realTimePacing = false)
    {
        _instrumentId = instrumentId;
        _contract = contract;
        _options = options ?? new SyntheticOptions();
        _clock = clock ?? SystemClock.Instance;
        _realTimePacing = realTimePacing;
    }

    public SourceProvider Source => SourceProvider.Mock;

    public ProviderCapabilities Capabilities { get; } = new(
        "Mock (Simulated)",
        DataCapabilities.Trades |
        DataCapabilities.TopOfBook |
        DataCapabilities.MarketByPriceDepth |
        DataCapabilities.ExchangeTimestamps |
        DataCapabilities.SequenceNumbers |
        DataCapabilities.AggressorSideFlags |
        DataCapabilities.HistoricalTrades |
        DataCapabilities.HistoricalBars);

    public ConnectionState ConnectionState => _state;

    public event Action<ConnectionState>? ConnectionStateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        SetState(ConnectionState.Connecting);
        SetState(ConnectionState.Connected);
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(Contract contract, SubscriptionOptions options, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async IAsyncEnumerable<MarketEvent> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var generator = new SyntheticSessionGenerator(_instrumentId, _contract, _clock.UtcNow, _options);
        DateTime? lastTs = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var ev = generator.Next();

            if (_realTimePacing && lastTs is { } prev)
            {
                var wait = ev.ExchangeTimestampUtc - prev;
                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }
                }
            }

            lastTs = ev.ExchangeTimestampUtc;
            yield return ev;
        }
    }

    public Task DisconnectAsync()
    {
        SetState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SetState(ConnectionState.Disconnected);
        return ValueTask.CompletedTask;
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        ConnectionStateChanged?.Invoke(state);
    }
}
