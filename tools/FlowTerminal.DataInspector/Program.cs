// FlowTerminal.DataInspector — read-only DuckDB inspection of a recorded session.
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Storage.Duck;
using FlowTerminal.Storage.Parquet;

if (args.Length < 4)
{
    Console.WriteLine("Usage: datainspector <dataRoot> <NQ|ES> <contractSymbol> <yyyy-MM-dd>");
    return 2;
}

if (!InstrumentRegistry.TryParseRoot(args[1], out var root))
{
    Console.WriteLine($"Unsupported root '{args[1]}'. Only NQ and ES are supported.");
    return 2;
}

var date = DateOnly.ParseExact(args[3], "yyyy-MM-dd");
var dir = new RecordingLayout(args[0]).SessionDirectory(root, args[2], date);

Console.WriteLine($"Session: {dir}");
Console.WriteLine($"Total events: {DuckDbInspector.CountEvents(dir):N0}");
Console.WriteLine("By event type (Type byte = MarketEventType):");
foreach (var row in DuckDbInspector.CountByType(dir))
{
    Console.WriteLine($"  {(FlowTerminal.Domain.Events.MarketEventType)row.Type,-16} {row.Count,12:N0}");
}

return 0;
