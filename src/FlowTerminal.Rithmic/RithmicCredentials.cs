namespace FlowTerminal.Rithmic;

/// <summary>Which Rithmic environment to connect to.</summary>
public enum RithmicEnvironment
{
    Test,
    Live,
}

/// <summary>
/// Rithmic sign-in details entered by the user. The <see cref="Password"/> is held only
/// for the lifetime of a connection attempt and is <b>never logged or persisted</b> — the
/// overridden <see cref="ToString"/> redacts it, and the credential store only saves the
/// non-secret fields. This carries data-feed credentials; Flow Terminal remains read-only
/// and never places orders.
/// </summary>
public sealed record RithmicCredentials
{
    public string Username { get; init; } = string.Empty;

    /// <summary>Session-only secret. Never written to disk, telemetry, or logs.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Rithmic system name (e.g. "Rithmic Test", "Rithmic Paper Trading").</summary>
    public string SystemName { get; init; } = string.Empty;

    /// <summary>Gateway / connection point (e.g. "Chicago Area"). Optional.</summary>
    public string Gateway { get; init; } = string.Empty;

    public RithmicEnvironment Environment { get; init; } = RithmicEnvironment.Test;

    /// <summary>The minimum needed to attempt a login.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(SystemName);

    /// <summary>Redacted rendering — the password is never included.</summary>
    public override string ToString() =>
        $"RithmicCredentials {{ Username = {Username}, System = {SystemName}, Gateway = {Gateway}, Environment = {Environment}, Password = *** }}";

    // Records generate PrintMembers for ToString; override it too so the secret can never
    // leak through the default record formatting.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Username = ").Append(Username)
               .Append(", System = ").Append(SystemName)
               .Append(", Gateway = ").Append(Gateway)
               .Append(", Environment = ").Append(Environment)
               .Append(", Password = ***");
        return true;
    }
}

/// <summary>Outcome of a Rithmic connection attempt.</summary>
public enum RithmicConnectionOutcome
{
    Connected,
    Failed,
    InvalidCredentials,

    /// <summary>The authorized Rithmic SDK is not compiled into this build (mock mode).</summary>
    SdkUnavailable,
}

/// <summary>Result of a connection attempt: an outcome plus a user-facing message (no secrets).</summary>
public readonly record struct RithmicConnectionResult(RithmicConnectionOutcome Outcome, string Message)
{
    public bool IsConnected => Outcome == RithmicConnectionOutcome.Connected;
}
