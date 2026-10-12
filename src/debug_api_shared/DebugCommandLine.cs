namespace Debug.Api;

/// <summary>
/// Reading the dev API's own flags out of the command line it was given.
///
/// <para>
/// Both halves of the dev API need this, and neither may reach the game's
/// LocalCommandLine (plan_devapi_interfaces §5, group G10), so it is here:
/// pure functions over the argument list the loader hands the constructor.
/// DebugCommandLineTests holds it equal to LocalCommandLine, case by case.
/// </para>
/// </summary>
public static class DebugCommandLine
{
    /// <summary>Whether <paramref name="param"/> was given, ignoring case.</summary>
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
    /// The argument after <paramref name="param"/>, or <paramref name="fallback"/>
    /// when the flag is absent, last, or followed by another flag or a console
    /// command (stock's ParmValue).
    /// </summary>
    public static string Value(IReadOnlyList<string> args, string param, string fallback)
    {
        if (string.IsNullOrEmpty(param))
            return fallback;

        for (int i = 0; i < args.Count - 1; i++)
        {
            if (!string.Equals(args[i], param, StringComparison.OrdinalIgnoreCase))
                continue;

            return args[i + 1].StartsWith('-') || args[i + 1].StartsWith('+') ? fallback : args[i + 1];
        }

        return fallback;
    }
}
