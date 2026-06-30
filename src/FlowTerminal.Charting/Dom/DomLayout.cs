using System.Globalization;

namespace FlowTerminal.Charting.Dom;

/// <summary>
/// One column's state inside an editable DOM layout: its type, whether it is shown, and
/// its width. Purely presentational — toggling, reordering, or resizing a column never
/// changes what the column means or adds any execution surface.
/// </summary>
public sealed class DomLayoutColumn
{
    public DomColumnType Type { get; }
    public bool Visible { get; set; }
    public double Width { get; set; }

    public DomLayoutColumn(DomColumnType type, bool visible, double width)
    {
        Type = type;
        Visible = visible;
        Width = width;
    }

    public DomColumnDescriptor Descriptor => DomColumnRegistry.For(Type);
}

/// <summary>
/// An editable, ordered DOM column layout. It wraps a preset's column set and lets the
/// user show/hide, reorder, and resize columns. It resolves to the visible column types
/// and widths the Skia ladder renders, and serialises to a compact string for template
/// persistence. The layout is the single source of truth for "what columns the DOM shows"
/// once the user starts customising — strictly observational, no order entry.
/// </summary>
public sealed class DomLayout
{
    /// <summary>Width bounds (px) the editor and serialiser clamp to.</summary>
    public const double MinWidth = 36;
    public const double MaxWidth = 220;

    private readonly List<DomLayoutColumn> _columns;

    public DomLayout(IEnumerable<DomLayoutColumn> columns) => _columns = columns.ToList();

    public IReadOnlyList<DomLayoutColumn> Columns => _columns;

    /// <summary>Number of currently visible columns.</summary>
    public int VisibleCount
    {
        get { int n = 0; foreach (var c in _columns) if (c.Visible) n++; return n; }
    }

    /// <summary>
    /// Builds a layout from a preset: the preset's columns come first (visible, in order)
    /// at their default widths, then every other known column follows, hidden. This gives
    /// the editor the full catalogue while preserving the preset's chosen layout.
    /// </summary>
    public static DomLayout FromPreset(DomPreset preset)
    {
        var list = new List<DomLayoutColumn>();
        var seen = new HashSet<DomColumnType>();
        foreach (var t in preset.Columns)
            if (seen.Add(t))
                list.Add(new DomLayoutColumn(t, true, DomColumnRegistry.For(t).DefaultWidth));
        foreach (var d in DomColumnRegistry.All)
            if (seen.Add(d.Type))
                list.Add(new DomLayoutColumn(d.Type, false, d.DefaultWidth));
        return new DomLayout(list);
    }

    /// <summary>The visible column types, in order — what the renderer's layout consumes.</summary>
    public IReadOnlyList<DomColumnType> ResolveColumns()
    {
        var r = new List<DomColumnType>(_columns.Count);
        foreach (var c in _columns) if (c.Visible) r.Add(c.Type);
        return r;
    }

    /// <summary>The visible columns' widths, aligned with <see cref="ResolveColumns"/>.</summary>
    public IReadOnlyList<double> ResolveWidths()
    {
        var r = new List<double>(_columns.Count);
        foreach (var c in _columns) if (c.Visible) r.Add(c.Width);
        return r;
    }

    private int IndexOf(DomColumnType type)
    {
        for (int i = 0; i < _columns.Count; i++) if (_columns[i].Type == type) return i;
        return -1;
    }

    /// <summary>Moves the column at <paramref name="index"/> one slot earlier; returns false at the top.</summary>
    public bool MoveUp(int index)
    {
        if (index <= 0 || index >= _columns.Count) return false;
        (_columns[index - 1], _columns[index]) = (_columns[index], _columns[index - 1]);
        return true;
    }

    /// <summary>Moves the column at <paramref name="index"/> one slot later; returns false at the bottom.</summary>
    public bool MoveDown(int index)
    {
        if (index < 0 || index >= _columns.Count - 1) return false;
        (_columns[index + 1], _columns[index]) = (_columns[index], _columns[index + 1]);
        return true;
    }

    public void SetVisible(DomColumnType type, bool visible)
    {
        int i = IndexOf(type);
        if (i >= 0) _columns[i].Visible = visible;
    }

    public void SetWidth(DomColumnType type, double width)
    {
        int i = IndexOf(type);
        if (i >= 0) _columns[i].Width = Math.Clamp(width, MinWidth, MaxWidth);
    }

    /// <summary>Compact, human-legible serialisation: <c>id:visible:width</c> tokens, comma-separated.</summary>
    public string Serialize()
    {
        var parts = new string[_columns.Count];
        for (int i = 0; i < _columns.Count; i++)
        {
            var c = _columns[i];
            parts[i] = string.Concat(
                c.Descriptor.Id, ":", c.Visible ? "1" : "0", ":",
                Math.Round(Math.Clamp(c.Width, MinWidth, MaxWidth)).ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(",", parts);
    }

    /// <summary>
    /// Parses a serialised layout. Unknown ids are skipped; any known columns missing from
    /// the string are appended hidden (so new columns appear in old templates). Returns null
    /// if nothing usable parsed, letting callers fall back to a preset.
    /// </summary>
    public static DomLayout? Deserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var list = new List<DomLayoutColumn>();
        var seen = new HashSet<DomColumnType>();
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = token.Split(':');
            if (f.Length < 2) continue;
            var desc = DomColumnRegistry.ById(f[0]);
            if (desc is null || !seen.Add(desc.Type)) continue;
            bool visible = f[1] == "1";
            double width = desc.DefaultWidth;
            if (f.Length >= 3 && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                width = Math.Clamp(w, MinWidth, MaxWidth);
            list.Add(new DomLayoutColumn(desc.Type, visible, width));
        }
        if (list.Count == 0) return null;
        foreach (var d in DomColumnRegistry.All)
            if (seen.Add(d.Type))
                list.Add(new DomLayoutColumn(d.Type, false, d.DefaultWidth));
        return new DomLayout(list);
    }
}
