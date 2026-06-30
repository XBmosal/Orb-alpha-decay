# Reliability & fault isolation

Flow Terminal favours **fault isolation** over fail-fast: one faulty component should
never take down the whole terminal, and canonical market events are never silently
dropped. This document records the boundaries that enforce that.

## Render faults are isolated per panel

Every Skia panel paints inside `RenderSafety.Guard` (`FlowTerminal.Charting`). If a draw
throws (a bad indicator, footprint, or heatmap frame), the panel:

1. does not let the exception reach WPF's render loop (which would become a fatal,
   whole-app `DispatcherUnhandledException`);
2. rebalances the canvas save/clip stack;
3. paints a calm, themed **"… view paused"** card (amber warning + explanation);
4. reports the fault to the host, which logs it **throttled** (`RenderGuard`,
   once per 30 s per panel) so the log is never flooded.

Other panels keep rendering normally. Wired into `SkiaChartHost`, `HeatmapHost`, and
`CvdHost`.

## Pipeline faults are isolated per event

`InstrumentPipeline.ProcessAsync` wraps the downstream analytics/book/UI call: a fault on
a single event is counted as `PipelineDiagnostics.ProcessingErrors` and skipped, and the
consumer keeps draining the channel. One bad event cannot freeze the feed.
`OperationCanceledException` still propagates so shutdown unwinds cleanly. The cardinal
invariant is preserved: `DroppedCanonicalEvents` stays 0 in normal operation — isolation
is not silent data loss, it is a counted, visible alarm.

## Process-level exception handling

`App` installs three handlers (all log via Serilog, 14-day rolling retention):

- `DispatcherUnhandledException` — UI-thread faults: log, show a diagnosable dialog, mark
  handled where safe.
- `AppDomain.UnhandledException` — last-resort domain faults.
- `TaskScheduler.UnobservedTaskException` — faulted background tasks (feed pump, storage,
  replay) are logged and marked observed so they never escalate to a crash.

Startup is wrapped in try/catch with a clear failure dialog.

## Tested

- `RenderSafetyTests` — successful draw runs; a throwing draw is isolated, paints an
  error state, and leaves the save-stack balanced.
- `InstrumentPipelineTests.Downstream_Fault_On_One_Event_Is_Isolated_And_Ingestion_Continues`
  — a fault on one event is counted and every later event still flows; no canonical drops.

## Read-only guarantee

None of the above adds order entry, accounts, positions, or execution. Flow Terminal
remains strictly observational.
