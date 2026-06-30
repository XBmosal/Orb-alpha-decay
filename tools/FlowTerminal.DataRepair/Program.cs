// FlowTerminal.DataRepair — detects partial/corrupt recordings and (optionally)
// quarantines them so the remaining valid parts replay cleanly. Reversible: bad
// files are moved to a _quarantine subfolder, never deleted.
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Storage.Parquet;
using FlowTerminal.Storage.Repair;

if (args.Length < 4)
{
    Console.WriteLine("Usage: datarepair <dataRoot> <NQ|ES> <contractSymbol> <yyyy-MM-dd> [--fix]");
    return 2;
}

if (!InstrumentRegistry.TryParseRoot(args[1], out var root))
{
    Console.WriteLine($"Unsupported root '{args[1]}'. Only NQ and ES are supported.");
    return 2;
}

var date = DateOnly.ParseExact(args[3], "yyyy-MM-dd");
var dir = new RecordingLayout(args[0]).SessionDirectory(root, args[2], date);
bool fix = args.Length > 4 && args[4] == "--fix";

var report = await new DataRepairService().ScanAsync(dir, quarantine: fix);
Console.WriteLine($"Session: {dir}");
Console.WriteLine($"Parts: {report.TotalParts}  valid: {report.ValidParts}  corrupt: {report.CorruptParts}  orphaned .tmp: {report.OrphanedTempFiles}");
if (fix)
{
    Console.WriteLine($"Quarantined {report.Quarantined} file(s) → {dir}/_quarantine");
}
else if (!report.IsHealthy)
{
    Console.WriteLine("Issues found. Re-run with --fix to quarantine bad files.");
}
Console.WriteLine(report.IsHealthy ? "Status: HEALTHY" : "Status: NEEDS REPAIR");
return report.IsHealthy ? 0 : 1;
