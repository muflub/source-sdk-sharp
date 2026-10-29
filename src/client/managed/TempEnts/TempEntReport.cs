using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// What has actually arrived and what actually played.
///
/// <para>
/// WHY IT EXISTS: gate 3 of plan_tmp.md Part 9 is a live join, and the only
/// thing a join by itself proves is that the tables paired. It does not say
/// that the blob decoded into the right bytes, that a managed player ran, or
/// that anything was drawn — and the difference between "the effect arrived and
/// nothing plays it" and "no effect arrived" is invisible from the console
/// otherwise, because both are silence.
/// </para>
///
/// <para>
/// So this prints the three numbers that tell those apart: how many arrived and
/// played, how many arrived and were dropped, and which classes have a player
/// at all. <c>cs_cl_te_dump</c> is the SCHEMA half of gate 2 and this is the
/// TRAFFIC half; a table that pairs perfectly and a counter that stays at zero
/// is a real state and one this port has to be able to see.
/// </para>
///
/// <para>
/// The client counters plan 9.1 asks for, one command earlier than the sandbox
/// pass that will drive all forty-four.
/// </para>
/// </summary>
[ConCommand("cs_cl_te_report",
    Help = "Temp entities received on this client: how many played, how many were dropped, "
         + "and which classes have a managed player")]
public sealed class TempEntReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientTempEnts _players;
    private readonly TempEntRegistry _registry;

    public TempEntReportCommand(ILog log, ClientTempEnts players, TempEntRegistry registry)
    {

        _log = log;
        _players = players;
        _registry = registry;
    }

    public void Execute(string args)
    {
        _log.Msg($"[SourceSharp/Client] temp entities: {_players.Played} played, " +
                $"{_players.Dropped} dropped\n");

        // THE BLAST PUSH, counted, because behaviour ledger row C1 was open for
        // most of this port and an explosion draws identically whether or not
        // it moved anything. A live join that reports "0 ragdolls pushed" over
        // a map full of bodies is the difference between a wired capability and
        // a working one, and nothing else on this client can tell them apart.
        if (_players.Player("CTEExplosion") is ClientTeExplosion explosion)
        {
            _log.Msg($"[SourceSharp/Client]   blast push (row C1): " +
                    $"{explosion.RagdollsPushed} ragdoll(s) pushed by " +
                    $"{explosion.BlastsThatPushed} explosion(s)\n");
        }

        foreach (TempEntType type in _registry.All)
        {
            // The handle is printed because it is the ONLY thing an arriving
            // effect carries, so a report that omitted it could not be lined up
            // against a dispatch that went nowhere.
            _log.Msg($"[SourceSharp/Client]   {type.Class.ClassName,-24} handle " +
                    $"{type.Handle,3}  {type.BlobBytes,3} blob byte(s)  " +
                    $"{(_players.PlayerNames.Contains(type.Class.ClassName) ? "played by C#" : "NO PLAYER")}" +
                    $"  {_players.PlayedOf(type.Class.ClassName)} played\n");
        }

        // One temp entity stands for every named effect, so its count alone
        // cannot say which arrived.
        if (_players.PlayerOf("CTEEffectDispatch") is ClientTeEffectDispatch dispatch && dispatch.PlayedByName.Count > 0)
        {
            _log.Msg("[SourceSharp/Client]   dispatched: " +
                    string.Join(", ", dispatch.PlayedByName.OrderBy(p => p.Key, StringComparer.Ordinal)
                                                           .Select(p => $"{p.Key} {p.Value}")) + "\n");
        }

        Describe();
    }

    /// <summary>
    /// The LAST blob that played, field by field, read back through the same
    /// declaration the server packed it with.
    ///
    /// <para>
    /// THIS IS THE HALF THE COUNTERS CANNOT GIVE. A count says an effect
    /// arrived; it does not say it arrived with the right numbers, and a
    /// coordinate that decoded at the wrong offset is a plausible value at
    /// somewhere else in the map. Firing at a known point and reading the point
    /// back is the round trip, and it is what this project's own note on
    /// verifying a datatable insists on: a value written on the server and
    /// CHECKED on the client, not printed and eyeballed.
    /// </para>
    ///
    /// <para>
    /// Generic over the declaration, so every class a later lane lands is
    /// readable here the day it lands with nothing added.
    /// </para>
    /// </summary>
    private void Describe()
    {
        TempEntType? last = _players.LastPlayed;

        if (last is null)
        {
            _log.Msg("[SourceSharp/Client]   (nothing has played yet)\n");
            return;
        }

        var reader = new TempEntReader(last, _players.LastBlob);

        _log.Msg($"[SourceSharp/Client] last played: {last.Class.ClassName}, " +
                $"{_players.LastBlob.Length} byte(s)\n");

        // By offset, which is wire order -- the order the server wrote them and
        // the order both tables carry their props in.
        foreach (TeField field in last.Fields.Values.OrderBy(f => f.Offset))
        {
            string value = field.Kind switch
            {
                TePropKind.Vector or TePropKind.QAngles => reader.Vector(field.Name).ToString(),
                TePropKind.Float or TePropKind.Angle    => reader.Float(field.Name).ToString("0.###"),
                TePropKind.Int when field.Elems > 1     => "(elements)",
                TePropKind.Int                          => reader.Int(field.Name).ToString(),
                _                                       => "(nested)",
            };

            // A field declared as N elements is ONE C# member and N props; the
            // vector reader is right for the three-float form and says so
            // rather than printing the first element as if it were the whole.
            if (field.Elems == 3 && field.Kind == TePropKind.Float)
                value = reader.Vector(field.Name).ToString();

            _log.Msg($"[SourceSharp/Client]   {field.Name,-20} @{field.Offset,3} = {value}\n");

            // AN ARRAY IS WHERE THE NUMBERS ACTUALLY ARE, so it is printed
            // element by element rather than as "(nested)".
            //
            // There is exactly one array in all 44 tables -- DT_TEBeamSpline's
            // sixteen COORD points -- and it belongs to the one class in the
            // inventory that DRAWS NOTHING (stock's whole PostDataUpdate is a
            // DevMsg). So for that class the counters say an effect arrived and
            // this is the only thing in the build that can say it arrived with
            // the right coordinates. Printing "(nested)" made the one temp
            // entity whose decode most needs checking the one that could not be
            // checked at all.
            //
            // ALL OF THEM, not m_nPoints of them: with a NULL length proxy every
            // element crosses the wire whatever the count says (plan 3.3), and a
            // stride error shows up at the FAR END of the run.
            if (field.Kind == TePropKind.Array)
            {
                for (int i = 0; i < field.Elems; i++)
                {
                    _log.Msg($"[SourceSharp/Client]     {field.Name}[{i}] " +
                            $"@{field.Offset + i * field.ElemStride,3} = " +
                            $"{reader.VectorAt(field.Name, i)}\n");
                }
            }
        }
    }
}
