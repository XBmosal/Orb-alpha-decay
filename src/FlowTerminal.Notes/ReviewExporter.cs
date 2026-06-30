using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FlowTerminal.Notes;

/// <summary>
/// Exports session-review data to CSV or JSON. These exports contain only manually
/// entered review material — never credentials and never broker-confirmed trades.
/// </summary>
public static class ReviewExporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string NotesToJson(IEnumerable<SessionNote> notes) => JsonSerializer.Serialize(notes, Json);

    public static string NotesToCsv(IEnumerable<SessionNote> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,TimestampUtc,Root,Contract,Tags,Text");
        foreach (var n in notes)
        {
            sb.Append(n.Id).Append(',')
              .Append(n.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(n.Root).Append(',')
              .Append(Csv(n.ContractSymbol)).Append(',')
              .Append(Csv(string.Join('|', n.Tags))).Append(',')
              .Append(Csv(n.Text)).AppendLine();
        }

        return sb.ToString();
    }

    public static string AnnotationsToCsv(IEnumerable<ManualAnnotation> annotations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,CreatedUtc,Root,Contract,Direction,EntryTicks,ExitTicks,StopTicks,TargetTicks,Outcome,RR,ManualUnverified,Notes");
        foreach (var a in annotations)
        {
            sb.Append(a.Id).Append(',')
              .Append(a.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.Root).Append(',')
              .Append(Csv(a.ContractSymbol)).Append(',')
              .Append(a.Direction).Append(',')
              .Append(a.EntryPriceTicks).Append(',')
              .Append(a.ExitPriceTicks?.ToString() ?? string.Empty).Append(',')
              .Append(a.StopPriceTicks?.ToString() ?? string.Empty).Append(',')
              .Append(a.TargetPriceTicks?.ToString() ?? string.Empty).Append(',')
              .Append(a.Outcome).Append(',')
              .Append(a.RiskRewardRatio?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
              .Append("true").Append(',')   // always manual/unverified
              .Append(Csv(a.Notes)).AppendLine();
        }

        return sb.ToString();
    }

    public static string BookmarksToJson(IEnumerable<Bookmark> bookmarks) => JsonSerializer.Serialize(bookmarks, Json);

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }
}
