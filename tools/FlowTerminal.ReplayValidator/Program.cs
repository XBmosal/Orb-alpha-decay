// FlowTerminal.ReplayValidator — replays a recorded session twice and compares
// hashes of final bars, footprints, CVD, profiles, and order-book state.
// The deterministic replay engine and checkpointing are implemented in Phase 5;
// this entry point fixes the CLI contract and exit-code semantics.
Console.WriteLine("Flow Terminal ReplayValidator");
Console.WriteLine("Usage: replayvalidator <session-directory>");
Console.WriteLine("Status: deterministic hash comparison lands in Phase 5 (Recording & Replay).");
return 0;
