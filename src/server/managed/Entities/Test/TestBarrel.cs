using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// ss_barrel: the entity the parallel hierarchy was proved with.
///
/// It began as the only user of a SECOND managed contract — its own registry,
/// its own base class, its own set of native callbacks — written while the
/// hierarchy was an experiment and it was not yet clear that the contract the
/// 109 map entities already spoke would fit. It does; they all run on the
/// hierarchy now. So this is an ordinary [ScriptedEntity] and the second
/// contract is gone.
///
/// <para>
/// It stays because it is still the only entity whose CLIENT half is written,
/// and the pairing — two managed objects for one entity, each picked
/// independently from the same networked classname — is worth being able to
/// check.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsBarrel, ScriptedEntityKind.Prop)]
public sealed class TestBarrel : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="TestBarrelVars"/>.</summary>
    [ScriptVars] public TestBarrelVars Vars = new();

    /// <summary>
    /// How many times it has thought — and it is <c>[SaveField]</c> because a
    /// hot reload does not restart the barrel.
    ///
    /// <para>
    /// <b>MEASURED, AND THE SYMPTOM WAS ON THE OTHER SIDE OF THE WIRE.</b> This
    /// is a plain managed field: neither a map key nor a networked value, so
    /// <c>SaveDeclaredState</c>'s automatic tier does not see it, and a
    /// <c>cs_sv_reload</c> gave the rebuilt instance a fresh zero.
    /// <see cref="State"/><c>.Thinks</c> — which IS networked, and lives in the
    /// blob, which survives the reload — was then written back DOWN, from
    /// whatever it had reached to 1.
    /// </para>
    ///
    /// <para>
    /// <c>ClientTestBarrel.OnDataChanged</c> checks that the count keeps up,
    /// because a frozen or garbled counter is one of the things a mislaid
    /// offset looks like. It read the drop as exactly that and printed
    /// <c>own table MISMATCH</c> three times per reload, in two separate live
    /// runs, beside four values that had all decoded perfectly — a FALSE ALARM
    /// in the one class whose job is to catch a real one, which is worse than
    /// no alarm because the next person discounts it.
    /// </para>
    ///
    /// <para>
    /// Carried rather than the check weakened: the barrel in the world did not
    /// restart, only its managed half was swapped, and a counter that resets
    /// under a reload is precisely the state
    /// <c>plan_entity.md</c>'s save tiers exist to stop losing. It reaches a
    /// <c>.sav</c> as well, through the same catalog and with no codec change.
    /// </para>
    /// </summary>
    [SaveField] private int _thinks;

    /// <summary>
    /// What this class replicates beyond origin, model, classname and angles.
    ///
    /// Bound to this entity by the registry, so a property set below writes
    /// straight into the blob the SendProps read from — the write IS the update.
    /// The client half holds one of the SAME type; see BarrelNetState.
    /// </summary>
    [NetState] public BarrelNetState State = new();

    public override void OnSpawn()
    {
        SetModel(Vars.Model.Length > 0 ? Vars.Model : TestBarrelVars.DefaultModel);

        // Ordinary assignments. Each one writes through into the entity's blob,
        // which is the memory the generated SendProps point into.
        State.Thinks      = 0;
        State.LastDamage  = 0f;
        State.SpawnOrigin = Origin;
        State.Broken      = false;
        State.NetModel    = Vars.Model.Length > 0 ? Vars.Model : TestBarrelVars.DefaultModel;

        Log.Msg($"[SourceSharp/C#] ss_barrel spawned with model '{Vars.Model}', {Health} health, " +
                   $"net state {(State.IsBound ? "written" : "NOT BOUND")}\n");
        SetNextThink(1f);
    }

    /// <summary>
    /// How many thinks are reported before this goes quiet. See
    /// <see cref="OnThink"/>: the think itself never stops.
    /// </summary>
    public const int QuietAfter = 3;

    public override void OnThink()
    {
        _thinks++;

        // The value that visibly moves: the client checks that what it decodes
        // keeps up rather than arriving once and stopping.
        State.Thinks = (short)_thinks;

        // QUIET AFTER THE FIRST FEW. The counter has to keep moving -- the
        // client's decode check reads it and a frozen value is what that test
        // is looking for -- but SAYING so once a second, forever, for every
        // barrel in the map, was most of the console. The first few prove the
        // think is running at all, which is the whole diagnostic value; the
        // ten-thousandth proves nothing that the first three did not.
        if (_thinks <= QuietAfter)
        {
            Log.Msg($"[SourceSharp/C#] ss_barrel think {_thinks}, {Health} health"
                  + (_thinks == QuietAfter ? " (quiet from here)" : "") + "\n");
        }

        SetNextThink(1f);
    }

    /// <summary>
    /// Half damage from bullets, full from everything else — a rule, and the
    /// kind of thing that has to live in C# for any of this to be worth it.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        int taken = (int)((damageType & DamageType.Bullet) != 0 ? damage * 0.5f : damage);

        State.LastDamage = taken;

        Log.Msg($"[SourceSharp/C#] ss_barrel took {taken} of {damage}\n");
        return taken;
    }

    public override void OnKilled(EntityHandle attacker)
    {
        // Written before Remove, which is the only order that can work: the
        // entity's blob is the entity's, and there is nothing to write into
        // once it is gone.
        State.Broken = true;

        Log.Msg("[SourceSharp/C#] ss_barrel destroyed\n");
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="TestBarrel"/> declares.
/// </summary>
[ScriptVars]
public partial class TestBarrelVars
{
    /// <summary>What it looks like when the map does not say.</summary>
    public const string DefaultModel = "models/props_c17/oildrum001.mdl";

    public string Model { get; set; } = DefaultModel;
}
