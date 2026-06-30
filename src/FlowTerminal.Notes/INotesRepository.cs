using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Notes;

/// <summary>A lightweight, timestamped session-review note. Not a brokerage trade journal.</summary>
public sealed record SessionNote(
    Guid Id,
    DateTime TimestampUtc,
    RootSymbol Root,
    string ContractSymbol,
    string Text,
    IReadOnlyList<string> Tags);

/// <summary>
/// Persistence boundary for session-review notes and bookmarks. Concrete SQLite
/// storage arrives in Phase 9; the interface is fixed here. These are manually
/// entered annotations and must never be represented as broker-confirmed trades.
/// </summary>
public interface INotesRepository
{
    Task AddAsync(SessionNote note, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionNote>> QueryAsync(RootSymbol? root, string? tag, CancellationToken cancellationToken);
}
