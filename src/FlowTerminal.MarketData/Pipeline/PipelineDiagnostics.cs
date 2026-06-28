namespace FlowTerminal.MarketData.Pipeline;

/// <summary>
/// Lock-free counters describing pipeline health. These feed the diagnostics
/// overlay. The cardinal invariant: <see cref="DroppedCanonicalEvents"/> must
/// remain zero in normal operation — canonical events are never silently dropped.
/// Visual updates, by contrast, may be coalesced or skipped freely.
/// </summary>
public sealed class PipelineDiagnostics
{
    private long _eventsIngested;
    private long _eventsProcessed;
    private long _droppedCanonicalEvents;
    private long _sequenceGaps;
    private long _duplicateEvents;
    private long _coalescedVisualUpdates;
    private long _droppedVisualUpdates;
    private long _currentQueueDepth;
    private long _maxQueueDepth;
    private long _invalidationCount;

    public long EventsIngested => Interlocked.Read(ref _eventsIngested);
    public long EventsProcessed => Interlocked.Read(ref _eventsProcessed);
    public long DroppedCanonicalEvents => Interlocked.Read(ref _droppedCanonicalEvents);
    public long SequenceGaps => Interlocked.Read(ref _sequenceGaps);
    public long DuplicateEvents => Interlocked.Read(ref _duplicateEvents);
    public long CoalescedVisualUpdates => Interlocked.Read(ref _coalescedVisualUpdates);
    public long DroppedVisualUpdates => Interlocked.Read(ref _droppedVisualUpdates);
    public long CurrentQueueDepth => Interlocked.Read(ref _currentQueueDepth);
    public long MaxQueueDepth => Interlocked.Read(ref _maxQueueDepth);
    public long InvalidationCount => Interlocked.Read(ref _invalidationCount);

    public void OnIngested() => Interlocked.Increment(ref _eventsIngested);
    public void OnProcessed() => Interlocked.Increment(ref _eventsProcessed);
    public void OnSequenceGap() => Interlocked.Increment(ref _sequenceGaps);
    public void OnDuplicate() => Interlocked.Increment(ref _duplicateEvents);
    public void OnInvalidation() => Interlocked.Increment(ref _invalidationCount);
    public void OnCoalescedVisualUpdate() => Interlocked.Increment(ref _coalescedVisualUpdates);
    public void OnDroppedVisualUpdate() => Interlocked.Increment(ref _droppedVisualUpdates);

    /// <summary>
    /// Records a dropped canonical event. This should only ever be called when the
    /// pipeline has already entered a visible invalid-data state and is about to
    /// resynchronize — it is an alarm, not a routine occurrence.
    /// </summary>
    public void OnDroppedCanonicalEvent() => Interlocked.Increment(ref _droppedCanonicalEvents);

    public void SetQueueDepth(long depth)
    {
        Interlocked.Exchange(ref _currentQueueDepth, depth);
        long max = Interlocked.Read(ref _maxQueueDepth);
        while (depth > max)
        {
            long prev = Interlocked.CompareExchange(ref _maxQueueDepth, depth, max);
            if (prev == max)
            {
                break;
            }

            max = prev;
        }
    }

    public DiagnosticsSnapshot Snapshot() => new(
        EventsIngested, EventsProcessed, DroppedCanonicalEvents, SequenceGaps, DuplicateEvents,
        CoalescedVisualUpdates, DroppedVisualUpdates, CurrentQueueDepth, MaxQueueDepth, InvalidationCount);
}

public readonly record struct DiagnosticsSnapshot(
    long EventsIngested,
    long EventsProcessed,
    long DroppedCanonicalEvents,
    long SequenceGaps,
    long DuplicateEvents,
    long CoalescedVisualUpdates,
    long DroppedVisualUpdates,
    long CurrentQueueDepth,
    long MaxQueueDepth,
    long InvalidationCount);
