using System.Globalization;
using System.Text;

using Debug.Api.Server.Interfaces;

namespace Debug.Api;

/// <summary>A quit asked for through <c>/command</c>, recorded before it ran.</summary>
/// <param name="Command">The command as sent.</param>
/// <param name="RequestSeq">Which API command it was, counting from 1 in this process.</param>
/// <param name="AtMs">When, on the exit recorders' clock; -1 when they are not armed.</param>
public sealed record QuitRequest(string Command, long RequestSeq, int AtMs);

/// <summary>
/// Why the process ended, decided from the facts the recorders hold.
///
/// <para>
/// THE DECISION IS HERE AND NOT IN NATIVE CODE. Native records a signal's
/// sender, SDL's quit and window-close events, and writes a fatal stop's record
/// itself (managed code never runs after one). Which of the facts was THE
/// reason is a rule, and rules are managed.
/// </para>
///
/// <para>
/// THE RULE: the earliest of the requests that carry a time -- a quit sent
/// through the dev API, a signal, a window close -- is the reason; the later
/// ones are its consequences (SDL turns SIGTERM into a quit event, and the
/// window's close into another). With none of those, a <c>] quit</c> line in
/// the console is a typed quit; an SDL quit with no cause seen is reported as
/// such; and nothing at all is a shutdown nobody here recorded asking for.
/// </para>
/// </summary>
public static class ExitRecord
{
    public const string ApiCommand = "api-command";
    public const string Signal = "signal";
    public const string WindowClose = "window-close";
    public const string Console = "console";
    public const string SdlQuit = "sdl-quit";
    public const string Shutdown = "shutdown";

    /// <summary>Whether a console command line asks the engine to quit.</summary>
    public static bool IsQuitCommand(string command)
    {
        foreach (string part in command.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string verb = part.Split(' ', 2)[0];
            if (verb.Equals("quit", StringComparison.OrdinalIgnoreCase) || verb.Equals("exit", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Whether a console line is the console's echo of a typed quit (<c>] quit</c>).</summary>
    public static bool IsTypedQuit(string line)
    {
        string text = line.TrimEnd('\n', '\r');
        return text.StartsWith("] ", StringComparison.Ordinal) && IsQuitCommand(text[2..]);
    }

    public static string SignalName(int signo) => signo switch
    {
        1 => "SIGHUP",
        2 => "SIGINT",
        15 => "SIGTERM",
        _ => $"signal {signo}",
    };

    /// <summary>
    /// The reason and its detail (a JSON object). <paramref name="senderCommandLine"/>
    /// names a pid's process, for a signal's sender that is still alive.
    /// </summary>
    public static (string Reason, string Detail) Decide(DebugExitFacts facts, QuitRequest? api, string? typedQuitLine,
                                                        Func<int, string?> senderCommandLine)
    {
        var timed = new List<(int AtMs, string Reason, string Detail)>();

        if (api is not null)
        {
            timed.Add((api.AtMs, ApiCommand,
                $"{{\"command\":{DebugApiServer.Quote(api.Command)},\"requestSeq\":{api.RequestSeq},\"atMs\":{api.AtMs}}}"));
        }

        if (facts.Signals > 0)
        {
            string? sender = senderCommandLine(facts.FirstPid);
            timed.Add((facts.FirstAtMs, Signal,
                $"{{\"signo\":{facts.FirstSigno},\"signal\":{DebugApiServer.Quote(SignalName(facts.FirstSigno))}," +
                $"\"senderPid\":{facts.FirstPid},\"senderUid\":{facts.FirstUid}," +
                $"\"sender\":{(sender is null ? "null" : DebugApiServer.Quote(sender))},\"atMs\":{facts.FirstAtMs}," +
                $"\"signals\":{facts.Signals}}}"));
        }

        if (facts.WindowCloses > 0)
            timed.Add((facts.WindowCloseAtMs, WindowClose, $"{{\"atMs\":{facts.WindowCloseAtMs}}}"));

        if (timed.Count > 0)
        {
            // Earliest first; an API request made before the recorders were
            // armed (-1) is earlier than anything they saw.
            var first = timed.OrderBy(t => t.AtMs).First();
            return (first.Reason, first.Detail);
        }

        if (typedQuitLine is not null)
            return (Console, $"{{\"line\":{DebugApiServer.Quote(typedQuitLine.TrimEnd('\n', '\r'))}}}");

        if (facts.SdlQuits > 0)
            return (SdlQuit, $"{{\"atMs\":{facts.SdlQuitAtMs},\"note\":\"SDL delivered a quit with no signal, window close or command seen\"}}");

        return (Shutdown, "{\"note\":\"the game shut down with no request recorded: no dev API quit, signal, window close or typed quit\"}");
    }

    /// <summary>Whether anything at all has asked the process to end yet.</summary>
    public static bool AnyRequest(DebugExitFacts facts, QuitRequest? api, string? typedQuitLine)
        => api is not null || typedQuitLine is not null || facts.Signals > 0 || facts.WindowCloses > 0 || facts.SdlQuits > 0;

    /// <summary>The recorders' facts, as the record carries them.</summary>
    public static string FactsJson(DebugExitFacts f)
        => new StringBuilder("{\"armed\":").Append(f.Armed ? "true" : "false")
            .Append(",\"signalsArmed\":").Append(f.SignalsArmed ? "true" : "false")
            .Append(",\"sdlArmed\":").Append(f.SdlArmed ? "true" : "false")
            .Append(",\"nowMs\":").Append(f.NowMs)
            .Append(",\"signals\":").Append(f.Signals)
            .Append(",\"first\":{\"signo\":").Append(f.FirstSigno).Append(",\"pid\":").Append(f.FirstPid)
            .Append(",\"uid\":").Append(f.FirstUid).Append(",\"atMs\":").Append(f.FirstAtMs).Append('}')
            .Append(",\"last\":{\"signo\":").Append(f.LastSigno).Append(",\"pid\":").Append(f.LastPid)
            .Append(",\"uid\":").Append(f.LastUid).Append(",\"atMs\":").Append(f.LastAtMs).Append('}')
            .Append(",\"sdlQuits\":").Append(f.SdlQuits).Append(",\"sdlQuitAtMs\":").Append(f.SdlQuitAtMs)
            .Append(",\"windowCloses\":").Append(f.WindowCloses).Append(",\"windowCloseAtMs\":").Append(f.WindowCloseAtMs)
            .Append(",\"previous\":{\"SIGTERM\":").Append(f.PrevTerm).Append(",\"SIGINT\":").Append(f.PrevInt)
            .Append(",\"SIGHUP\":").Append(f.PrevHup).Append("}}")
            .ToString();

    /// <summary>A pid's command line from /proc, or null when it has gone (or there is no /proc).</summary>
    public static string? CommandLineOf(int pid)
    {
        if (pid <= 0)
            return null;

        try
        {
            string raw = File.ReadAllText($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/cmdline");
            string line = raw.Replace('\0', ' ').Trim();
            return line.Length == 0 ? null : (line.Length > 200 ? line[..200] : line);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
