using System.IO;
using System.Text.Json;
using FlowTerminal.Rithmic;

namespace FlowTerminal.App;

/// <summary>The non-secret Rithmic sign-in fields worth remembering between sessions.</summary>
public sealed record RithmicRememberedLogin(
    string Username, string SystemName, string Gateway, RithmicEnvironment Environment);

/// <summary>
/// Persists only the <b>non-secret</b> Rithmic login fields (username, system, gateway,
/// environment) so the form can be pre-filled. The password is deliberately never written
/// to disk. All failures degrade to a no-op / empty result so a corrupt file never blocks
/// the UI.
/// </summary>
public sealed class RithmicCredentialStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public RithmicCredentialStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowTerminal");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "rithmic.json");
    }

    public RithmicRememberedLogin? Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<RithmicRememberedLogin>(File.ReadAllText(_path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Saves the non-secret fields. The <paramref name="login"/> record has no password field.</summary>
    public void Save(RithmicRememberedLogin login)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(login, Options));
        }
        catch
        {
            // Persisting the remembered login is best-effort; never surface an I/O error.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch
        {
            // ignore
        }
    }
}
