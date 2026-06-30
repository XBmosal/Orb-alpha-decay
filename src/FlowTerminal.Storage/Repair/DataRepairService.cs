using Parquet.Serialization;
using FlowTerminal.Storage.Parquet;

namespace FlowTerminal.Storage.Repair;

public sealed record RepairReport(
    int TotalParts,
    int ValidParts,
    int CorruptParts,
    int OrphanedTempFiles,
    int Quarantined,
    IReadOnlyList<string> QuarantinedPaths)
{
    public bool IsHealthy => CorruptParts == 0 && OrphanedTempFiles == 0;
}

/// <summary>
/// Detects and repairs partial/corrupt recordings. It validates each Parquet event
/// part by attempting to read it, treats leftover <c>.tmp</c> files as interrupted
/// writes, and (when <paramref name="quarantine"/> is set) moves bad files into a
/// <c>_quarantine</c> subfolder so the remaining valid parts replay cleanly. It
/// never deletes data outright — quarantine is reversible.
/// </summary>
public sealed class DataRepairService
{
    public async Task<RepairReport> ScanAsync(string sessionDirectory, bool quarantine, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sessionDirectory))
        {
            return new RepairReport(0, 0, 0, 0, 0, Array.Empty<string>());
        }

        var quarantineDir = Path.Combine(sessionDirectory, "_quarantine");
        var quarantined = new List<string>();

        // Orphaned temp files = interrupted writes.
        var tempFiles = Directory.GetFiles(sessionDirectory, "*.tmp");
        foreach (var tmp in tempFiles)
        {
            if (quarantine)
            {
                Move(tmp, quarantineDir, quarantined);
            }
        }

        int total = 0, valid = 0, corrupt = 0;
        foreach (var part in Directory.GetFiles(sessionDirectory, "events-*.parquet"))
        {
            total++;
            if (await IsReadableAsync(part, cancellationToken).ConfigureAwait(false))
            {
                valid++;
            }
            else
            {
                corrupt++;
                if (quarantine)
                {
                    Move(part, quarantineDir, quarantined);
                }
            }
        }

        return new RepairReport(total, valid, corrupt, tempFiles.Length, quarantined.Count, quarantined);
    }

    private static async Task<bool> IsReadableAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var rows = await ParquetSerializer.DeserializeAsync<MarketEventRecord>(stream, cancellationToken: ct).ConfigureAwait(false);
            return rows is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Move(string file, string quarantineDir, List<string> quarantined)
    {
        Directory.CreateDirectory(quarantineDir);
        var dest = Path.Combine(quarantineDir, Path.GetFileName(file));
        if (File.Exists(dest))
        {
            dest = Path.Combine(quarantineDir, $"{Path.GetFileNameWithoutExtension(file)}-{Guid.NewGuid():N}{Path.GetExtension(file)}");
        }

        File.Move(file, dest);
        quarantined.Add(dest);
    }
}
