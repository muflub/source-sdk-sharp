namespace SourceSharp.Abi;

/// <summary>
/// THIS PROCESS' OWN COMMAND LINE, read in managed rather than through the ABI.
///
/// <para>
/// <b>BOTH SIDES READ THEIR OWN.</b> The engine's command line reached
/// managed through two EngineApi slots that were filled on the server and null
/// on the client, where they answered a plausible <c>false</c> to everything.
/// The client read argv here instead, and since plan_engineapi_vtables the
/// server does too and the two slots are retired. Reading it here costs no
/// slot, no ABI version and no native build.
/// </para>
///
/// <para>
/// The matching is the engine's: a parameter is present if it appears as its own
/// argument, and its value is the argument after it. Case-insensitive, because
/// <c>ICommandLine::FindParm</c> is.
/// </para>
///
/// <para>
/// The matching lives here as static functions of the argument list they are
/// given; the READING is <see cref="ProcessCommandLine"/>, a service the client
/// module registers and hands to whatever asks. It was a static cache here.
/// </para>
/// </summary>
public static class LocalCommandLine
{
    /// <summary>
    /// Whether a parameter appears in a given argument list. The list is a
    /// parameter so a test can ask the question without being the process.
    /// </summary>
    public static bool Has(IReadOnlyList<string> args, string param)
    {
        if (string.IsNullOrEmpty(param))
            return false;

        foreach (string arg in args)
        {
            if (string.Equals(arg, param, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The argument after a parameter, or the fallback.
    ///
    /// <para>
    /// A parameter at the very end of the line has no value and gets the
    /// fallback, rather than reading past the end — and so does one followed by
    /// another parameter, because <c>-debuguihost -debugui</c> means somebody left
    /// the host off and not that the host is called "-debugui".
    /// </para>
    /// </summary>
    public static string Value(IReadOnlyList<string> args, string param, string fallback)
    {
        if (string.IsNullOrEmpty(param))
            return fallback;

        for (int i = 0; i < args.Count - 1; i++)
        {
            if (!string.Equals(args[i], param, StringComparison.OrdinalIgnoreCase))
                continue;

            // A flag or a console command is not a value (stock's ParmValue).
            return args[i + 1].StartsWith('-') || args[i + 1].StartsWith('+') ? fallback : args[i + 1];
        }

        return fallback;
    }
}
