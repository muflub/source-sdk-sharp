using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_playercmds</c> — THE READOUT THAT LETS STEP 3d's CLAIM FAIL.
///
/// <para>
/// The claim is "N commands, one crossing": one <c>ServerPlayerCommands</c>
/// crossing per <c>ProcessUsercmds</c> however many commands the packet
/// carried, where the path it replaced cost three crossings per command.
/// </para>
///
/// <para>
/// <b>THE CROSSING COUNTER ALONE CANNOT SAY THAT.</b> A build that queued
/// commands and then never ran them would show exactly the same one crossing
/// per packet and would read as a triumph. This is the same shape 1g added
/// <c>status.think</c> for and 6a added <c>cs_cl_interp</c> for: a number that
/// moves with the WORK, not with the plumbing. <c>ran</c> is incremented inside
/// the managed run loop, once per command it actually put through
/// <c>PlayerRunMove</c>, so a loop that stopped running commands prints zero
/// and any gate reading it fails.
/// </para>
///
/// <para>
/// <c>queued</c> beside <c>ran</c> is the second half: they should track each
/// other, and a growing gap is a queue filling up faster than it drains.
/// </para>
/// </summary>
[ConCommand("cs_sv_playercmds",
    Help = "Player command batching: PlayerCommands crossings, commands queued, commands actually "
         + "run, and commands per crossing. 'reset' zeroes them.")]
public sealed class PlayerCommandsReport : IConCommand
{
    private readonly IEngineConsole _console;
    private readonly PlayerCommandQueue _queue;

    /// <param name="console">Where the report goes.</param>
    /// <param name="queue">This composition's queues and counters (statics to zero).</param>
    public PlayerCommandsReport(IEngineConsole console, PlayerCommandQueue queue)
    {
        _console = console;
        _queue = queue;
    }

    public void Execute(string args)
    {
        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            _queue.ResetCounters();
            _console.Msg("[playercmds] counters reset\n");
            return;
        }

        var (batches, queued, ran) = _queue.Counters();

        _console.Msg(Line(batches, queued, ran));
    }

    /// <summary>
    /// The line itself, as a pure function so a fact can assert its shape
    /// without a console.
    ///
    /// <para>
    /// ONE LINE with every number on it, because a gate reads it out of the log
    /// by prefix and a report split over lines is a report a reader can take
    /// half of.
    /// </para>
    /// </summary>
    public static string Line(long batches, long queued, long ran)
    {
        string perBatch = batches > 0
            ? ((double)ran / batches).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            : "0.000";

        return $"[playercmds] batches={batches} queued={queued} ran={ran} ranPerBatch={perBatch}\n";
    }
}
