using FlowTerminal.Domain.Events;

namespace FlowTerminal.MarketData.Abstractions;

/// <summary>
/// Durably records canonical events as they pass through the pipeline. Recorders
/// buffer and write in batches — they must never rewrite an entire session file
/// per event. The concrete Parquet recorder lives in the Storage layer.
/// </summary>
public interface IMarketDataRecorder : IAsyncDisposable
{
    bool IsRecording { get; }

    /// <summary>Records a single canonical event. Cheap and non-blocking; buffering happens internally.</summary>
    void Record(in MarketEvent marketEvent);

    /// <summary>Flushes buffered events to durable storage.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken);
}

/// <summary>A recorder that discards everything. Used when recording is disabled.</summary>
public sealed class NullMarketDataRecorder : IMarketDataRecorder
{
    public static NullMarketDataRecorder Instance { get; } = new();

    public bool IsRecording => false;

    public void Record(in MarketEvent marketEvent)
    {
        // Intentionally no-op.
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
