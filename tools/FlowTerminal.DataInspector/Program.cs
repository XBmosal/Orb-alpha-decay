// FlowTerminal.DataInspector — inspects recorded Parquet sessions (trades/depth/bars).
// Full DuckDB-backed inspection is implemented in Phase 5; this entry point fixes the CLI contract.
Console.WriteLine("Flow Terminal DataInspector");
Console.WriteLine("Usage: datainspector <session-directory>");
Console.WriteLine("Status: Parquet/DuckDB inspection lands in Phase 5 (Recording & Replay).");
return 0;
