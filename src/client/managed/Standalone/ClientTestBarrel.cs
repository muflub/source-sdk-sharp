using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of ss_barrel.
///
/// <para>
/// It exists to show the pairing works — the server's TestBarrel and this one
/// are two objects for one entity, created independently on each side from the
/// same networked classname.
/// </para>
///
/// <para>
/// And now to show the CLASS'S OWN table works. ss_barrel is the first entity
/// on the hierarchy with a SendTable of its own: the four props every entity
/// there shares, plus five it declared in shared code. This side decodes them
/// and CHECKS them rather than printing them, because a datatable that pairs
/// and decodes wrongly is the failure with no other symptom — the client joins,
/// the entity exists, and its values are quietly somebody else's.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsBarrel)]
public sealed class ClientTestBarrel : ClientStandaloneEntity
{
    public ClientTestBarrel(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.SsBarrel;

    /// <summary>
    /// The same type the server writes through, and the registry fills it in
    /// from each update. Unbound on this side: the blob arrives borrowed, valid
    /// only for the length of one call, so there is nothing to hold a view over
    /// and the values are copied out instead.
    /// </summary>
    [NetState] public BarrelNetState State = new();

    private int _updates;

    private bool _saidWentBack;

    public override void OnSpawn()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} arrived\n");

    public override void OnRemoved()
        => Log.Msg($"[SourceSharp/C# client] {ClassName} gone\n");

    /// <summary>
    /// Decodes the class's own state and says whether it makes sense.
    ///
    /// What is checked is what a WRONG OFFSET would break and a right one
    /// cannot: the model string is the one the server was asked for, the spawn
    /// origin is not the zero vector, and the think count only ever goes up.
    /// A field landing on the wrong offset shows as one of these failing rather
    /// than as nothing at all.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        // Decoded by the base before this runs; the properties below are reads
        // of the copy it made.
        int    thinks      = State.Thinks;
        float  lastDamage  = State.LastDamage;
        Vector spawnOrigin = State.SpawnOrigin;
        bool   broken      = State.Broken;
        string model       = State.NetModel;

        // WHICH CLAUSE, NOT WHETHER. This used to be one `bool sane` of four
        // ANDed terms, and the line it printed was the four VALUES -- so a
        // MISMATCH read as `model='models/...' spawnOrigin=(-3747, 2894, 40)
        // thinks=1 lastDamage=0.00 -- own table MISMATCH`, four healthy-looking
        // values and no reason. It took a lane to work out which term had
        // failed, on a run that had already scrolled. The clause names itself
        // now, and the values stay beside it.
        string? failed = Failure(model, spawnOrigin, thinks, lastDamage);

        // THE COUNTER GOING BACKWARDS IS REPORTED, NOT FAILED ON. It is a
        // SERVER fact -- something restarted the count -- and the one thing
        // that legitimately does it is a hot reload. Failing on it is what
        // printed three MISMATCH lines per reload in two live runs, beside four
        // fields that had all decoded perfectly. Once per instance, because it
        // is an observation and not an alarm.
        if (thinks < _updates && !_saidWentBack)
        {
            _saidWentBack = true;
            Log.Msg($"[SourceSharp/C# client] {ClassName}: think count went {_updates} -> " +
                       $"{thinks}. That is the SERVER's counter restarting, not a decode "  +
                       $"fault -- every other field on this update decoded correctly. If no "  +
                       $"reload happened, TestBarrel._thinks has stopped being carried.\n");
        }

        _updates = thinks;

        // Only the first update and then sparingly: this arrives on every
        // change, and a barrel that thinks once a second would otherwise write
        // a line a second per entity for the length of the map.
        if (updateType == DataUpdateType.Created || failed is not null)
        {
            Log.Msg($"[SourceSharp/C# client] {ClassName} {updateType}: " +
                       $"thinks={thinks} lastDamage={lastDamage:F2} spawnOrigin={spawnOrigin} " +
                       $"broken={broken} model='{model}' -- own table " +
                       $"{(failed is null ? "OK" : "MISMATCH: " + failed)}\n");
        }

        if (failed is not null)
        {
            Log.Warning($"[SourceSharp/C# client] {ClassName}: the class's own props do not " +
                           $"decode sensibly ({failed}). The two tables paired and disagree " +
                           $"about where the fields are.\n");
        }
    }

    /// <summary>
    /// The clause that failed, or null when every one held.
    ///
    /// <para>
    /// SEPARATE AND PURE so a test can drive it. The values reach it as
    /// arguments rather than being read from <see cref="State"/>, because the
    /// thing under test is the PREDICATE and a test that had to bind a net
    /// state to exercise it would be testing the binding.
    /// </para>
    ///
    /// <para>
    /// Ordered strongest first. The model is the only field whose width is a
    /// buffer rather than a number, so it is the one a mislaid offset garbles
    /// visibly instead of turning into a plausible number.
    /// </para>
    /// </summary>
    internal static string? Failure(string model, Vector spawnOrigin, int thinks,
                                    float lastDamage)
    {
        if (!model.StartsWith("models/", StringComparison.Ordinal))
            return $"model '{model}' is not a model path";

        if (spawnOrigin.X == 0f && spawnOrigin.Y == 0f && spawnOrigin.Z == 0f)
            return "spawnOrigin is the zero vector";

        if (lastDamage < 0f || float.IsNaN(lastDamage))
            return $"lastDamage {lastDamage:F2} is not a damage";

        // A NEGATIVE COUNT IS THE DECODE FAULT; A SMALLER ONE IS NOT.
        //
        // This clause used to be `thinks >= previous - 1`, and a hot reload
        // broke it honestly: TestBarrel._thinks was a plain managed field that
        // the reload did not carry, so the server's counter restarted and the
        // networked value went DOWN. Three MISMATCH lines per reload, in two
        // separate live runs, with every other field decoding perfectly.
        //
        // _thinks is [SaveField] now, so the counter really does keep up. The
        // clause is kept -- a frozen or garbled counter is a thing a wrong
        // offset produces -- but it asks the question the wire can answer: the
        // field is a short, and a short read at the wrong offset is negative
        // half the time. "It went down" is a SERVER fact and is reported, not
        // failed on, because this class must never cry wolf: a false alarm here
        // is worse than no alarm, since the next person discounts the real one.
        if (thinks < 0)
            return $"thinks {thinks} is negative";

        return null;
    }
}
