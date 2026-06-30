namespace FlowTerminal.Storage;

/// <summary>
/// Persistence boundaries for application metadata. Concrete SQLite-backed
/// implementations arrive in Phase 5; an in-memory implementation backs Phase 1
/// so the shell and tests have working persistence semantics immediately.
/// </summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}

public interface IWorkspaceRepository
{
    Task SaveAsync(string name, string json, CancellationToken cancellationToken);

    Task<string?> LoadAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Thread-safe in-memory settings store used by mock mode and tests.</summary>
public sealed class InMemorySettingsRepository : ISettingsRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _store = new();

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory workspace store used by mock mode and tests.</summary>
public sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _store = new();

    public Task SaveAsync(string name, string json, CancellationToken cancellationToken)
    {
        _store[name] = json;
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_store.TryGetValue(name, out var v) ? v : null);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_store.Keys.OrderBy(k => k).ToList());
}
