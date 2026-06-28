// FlowTerminal.ReplayValidator — replays a recorded session twice and compares a
// hash of the reconstructed order book + bars + CVD + profile + VWAP. Exit code 0
// when deterministic, 1 on mismatch, 2 on usage error.
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.Replay;
using FlowTerminal.Storage.Parquet;

if (args.Length < 4)
{
    Console.WriteLine("Usage: replayvalidator <dataRoot> <NQ|ES> <contractSymbol> <yyyy-MM-dd>");
    return 2;
}

if (!InstrumentRegistry.TryParseRoot(args[1], out var root))
{
    Console.WriteLine($"Unsupported root '{args[1]}'. Only NQ and ES are supported.");
    return 2;
}

var date = DateOnly.ParseExact(args[3], "yyyy-MM-dd");
var contractSymbol = args[2];
var layout = new RecordingLayout(args[0]);
var source = new ParquetReplaySource(layout, root, contractSymbol, date);

Contract.TryParse(contractSymbol, out var contract);
var request = new ReplayRequest(contract ?? new Contract(root, QuarterlyMonth.December, date.Year), DateTime.MinValue, DateTime.MaxValue);

var report = await new ReplayValidator(source, request).ValidateAsync();
Console.WriteLine(report.Describe());
if (source.CorruptParts > 0)
{
    Console.WriteLine($"WARNING: {source.CorruptParts} corrupt/partial part file(s) were skipped.");
}

return report.Match ? 0 : 1;
