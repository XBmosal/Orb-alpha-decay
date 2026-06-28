using System.Threading.Channels;
using FlowTerminal.Domain.Events;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.MarketData.Pipeline;

/// <summary>
/// A single deterministic, single-writer processor for one instrument. Events are
/// enqueued onto a <em>bounded</em> channel; a dedicated consumer drains them in
/// order, validates sequencing, records them, and forwards them downstream.
///
/// Backpressure policy (canonical events are never silently dropped):
///   - The channel is bounded with <see cref="BoundedChannelFullMode.Wait"/>, so a
///     fast producer is slowed rather than losing data.
///   - When depth crosses the high-water mark, <see cref="NearCapacity"/> fires so
///     the UI can shed optional visual work; ingestion continues unharmed.
///   - On a detected sequence gap, a SequenceGap event is emitted downstream and
///     diagnostics record an invalidation, signalling consumers to resynchronize.
/// </summary>
public sealed class InstrumentPipeline : IAsyncDisposable
{
    private readonly Channel<MarketEvent> _channel;
    private readonly SequenceValidator _sequenceValidator = new();
    private readonly IMarketDataRecorder _recorder;
    private readonly PipelineDiagnostics _diagnostics;
    private readonly Func<MarketEvent, CancellationToken, ValueTask> _downstream;
    private readonly int _highWaterMark;
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumer;
    private bool _warnedNearCapacity;

    public InstrumentPipeline(
        int instrumentId,
        Func<MarketEvent, CancellationToken, ValueTask> downstream,
        PipelineDiagnostics diagnostics,
        IMarketDataRecorder? recorder = null,
        int capacity = 65_536)
    {
        if (capacity < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity too small to be useful.");
        }

        InstrumentId = instrumentId;
        _downstream = downstream ?? throw new ArgumentNullException(nameof(downstream));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _recorder = recorder ?? NullMarketDataRecorder.Instance;
        _highWaterMark = (int)(capacity * 0.8);

        _channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // never drop canonical events
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public int InstrumentId { get; }

    /// <summary>Raised (at most once until depth recedes) when the queue nears capacity.</summary>
    public event Action<int>? NearCapacity;

    /// <summary>Starts the single-reader consumer loop. Idempotent.</summary>
    public void Start()
    {
        _consumer ??= Task.Run(() => ConsumeAsync(_cts.Token));
    }

    /// <summary>
    /// Enqueues a canonical event, applying bounded backpressure. Returns when the
    /// event is accepted; never discards it.
    /// </summary>
    public async ValueTask EnqueueAsync(MarketEvent marketEvent, CancellationToken cancellationToken)
    {
        _diagnostics.OnIngested();
        await _channel.Writer.WriteAsync(marketEvent, cancellationToken).ConfigureAwait(false);
        ObserveDepth();
    }

    /// <summary>Non-blocking enqueue; returns false only if the channel is full (caller must then await EnqueueAsync).</summary>
    public bool TryEnqueue(MarketEvent marketEvent)
    {
        if (_channel.Writer.TryWrite(marketEvent))
        {
            _diagnostics.OnIngested();
            ObserveDepth();
            return true;
        }

        return false;
    }

    public void Complete() => _channel.Writer.TryComplete();

    private void ObserveDepth()
    {
        int depth = _channel.Reader.Count;
        _diagnostics.SetQueueDepth(depth);

        if (depth >= _highWaterMark)
        {
            if (!_warnedNearCapacity)
            {
                _warnedNearCapacity = true;
                NearCapacity?.Invoke(InstrumentId);
            }
        }
        else if (depth < _highWaterMark / 2)
        {
            _warnedNearCapacity = false;
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var marketEvent in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await ProcessAsync(marketEvent, cancellationToken).ConfigureAwait(false);
                _diagnostics.SetQueueDepth(_channel.Reader.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async ValueTask ProcessAsync(MarketEvent marketEvent, CancellationToken cancellationToken)
    {
        // 1) Sequence validation.
        var result = _sequenceValidator.Check(in marketEvent);
        switch (result)
        {
            case SequenceResult.Duplicate:
                _diagnostics.OnDuplicate();
                return; // dropping a duplicate is correct, not data loss

            case SequenceResult.Gap:
                _diagnostics.OnSequenceGap();
                _diagnostics.OnInvalidation();
                // Surface the gap as a canonical control event so the book engine
                // can invalidate and resynchronize from a snapshot.
                var gap = MarketEvent.Control(
                    marketEvent.InstrumentId, marketEvent.Root, marketEvent.ContractSymbol, marketEvent.Exchange,
                    MarketEventType.SequenceGap, marketEvent.ExchangeTimestampUtc, marketEvent.ReceiveTimestampUtc,
                    marketEvent.ExchangeSequence, source: marketEvent.Source);
                await _downstream(gap, cancellationToken).ConfigureAwait(false);
                break;
        }

        // 2) Recording (buffered; does not block ingestion meaningfully).
        _recorder.Record(in marketEvent);

        // 3) Downstream analytics / book / UI snapshot publisher.
        await _downstream(marketEvent, cancellationToken).ConfigureAwait(false);

        _diagnostics.OnProcessed();
    }

    public async ValueTask DisposeAsync()
    {
        Complete();
        _cts.Cancel();
        if (_consumer is not null)
        {
            try
            {
                await _consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        _cts.Dispose();
    }
}
