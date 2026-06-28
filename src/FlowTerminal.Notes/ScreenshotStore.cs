using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Notes;

public sealed record ScreenshotRef(string FilePath, DateTime CapturedUtc, RootSymbol Root, string ContractSymbol);

/// <summary>
/// Persists chart screenshots (PNG bytes captured by the UI) to the screenshots
/// directory with a deterministic, sortable filename. The store only writes bytes —
/// it never embeds credentials, and exports never include sensitive data.
/// </summary>
public sealed class ScreenshotStore
{
    private readonly string _directory;

    public ScreenshotStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ScreenshotRef Save(byte[] pngBytes, RootSymbol root, string contractSymbol, DateTime capturedUtc)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        string name = $"{capturedUtc:yyyyMMdd-HHmmss-fff}_{root}_{Sanitize(contractSymbol)}.png";
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, pngBytes);
        return new ScreenshotRef(path, capturedUtc, root, contractSymbol);
    }

    public IReadOnlyList<string> List() =>
        Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "*.png").OrderBy(p => p, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    private static string Sanitize(string s) =>
        string.Concat(s.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
}
