namespace FlowTerminal.Rithmic;

/// <summary>
/// Reports whether the application was compiled with the authorized Rithmic SDK
/// integration enabled. The Rithmic adapter is isolated behind the
/// <c>RITHMIC_SDK</c> compile-time symbol so that the normal (mock) build never
/// requires any proprietary file. See <c>lib/rithmic/README.md</c> for how to
/// install the authorized SDK and enable the Rithmic build profile.
/// </summary>
public static class RithmicAvailability
{
    public static bool IsCompiledIn =>
#if RITHMIC_SDK
        true;
#else
        false;
#endif

    /// <summary>A human-readable status line for the diagnostics panel.</summary>
    public static string StatusDescription => IsCompiledIn
        ? "Rithmic adapter compiled in (RITHMIC_SDK). Authorized SDK wiring required to connect."
        : "Rithmic adapter not compiled in. Running in mock/replay mode. See lib/rithmic/README.md.";
}

/// <summary>
/// Thrown when Rithmic functionality is requested but the authorized SDK is not
/// installed/compiled in. It is never thrown in normal mock operation.
/// </summary>
public sealed class RithmicSdkUnavailableException : InvalidOperationException
{
    public RithmicSdkUnavailableException()
        : base("The authorized Rithmic SDK is not available. Flow Terminal is running without live Rithmic data. " +
               "Install the authorized SDK and enable the Rithmic build profile (see lib/rithmic/README.md), " +
               "or use Mock/Replay mode.")
    {
    }
}
