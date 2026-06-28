# Performance

Performance values are **measurable goals**, not guarantees. Numbers below are
reproduced from the repository's own benchmarks/tests — re-run them on your
hardware.

## Targets
- ≥ 50,000 normalized events/sec in benchmark mode; tolerate higher bursts.
- **Zero** silently dropped canonical events.
- Stable memory across an 8-hour synthetic session; no unbounded queue/cache growth.
- Active average CPU preferably < 15%; GPU < 10% at 30 FPS; memory < 1.5 GB
  (ordinary two-instrument session). These are validated in later phases.

## Measured so far (Phase 1)
`FlowTerminal.PerformanceTests.ThroughputTests` pushes **500,000** synthetic
events through a single `InstrumentPipeline`:

```
Processed 500,000 events in ~573 ms = ~872,000 events/sec   (Debug, Linux CI-class machine)
Max queue depth: 65,536, dropped canonical: 0
```

The test asserts ≥ 50,000 events/sec **and** zero dropped canonical events. The
`PipelineThroughputBenchmark` (BenchmarkDotNet) provides Release-mode figures with
allocation diagnostics:
```powershell
dotnet run -c Release --project benchmarks/FlowTerminal.Benchmarks
```

## Techniques used
- Bounded `System.Threading.Channels` with `Wait` full-mode (backpressure, no drops).
- One single-writer processor per instrument; no global lock on the hot path.
- `readonly struct` events; string fields are shared references (no per-event
  string allocation).
- Self-contained PRNG; no LINQ/reflection on the hot path.
- Lock-free diagnostics counters.

## Planned (later phases)
Ring buffers, `ArrayPool`/`Span`/`Memory`, object pooling, incremental indicators,
batched Parquet writes, cached/dirty-region SkiaSharp rendering, level-of-detail,
30 FPS frame limiting, and the live diagnostics overlay.
