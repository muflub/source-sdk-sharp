//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// trigger_stun (stock's <c>CTriggerStun</c>, tf_triggers.cpp:25-167): the
/// first touch arms a <see cref="StunInterval"/> think after
/// <c>trigger_delay</c>, and the think stuns everyone inside — for
/// <c>stun_duration</c> seconds, slowed by <c>move_speed_reduction</c>, the
/// type key picking controls or loser state and the effects key dropping the
/// stun particles — as long as anyone remains. A player leaving before ever
/// having been stunned is caught by the way out.
///
/// <para>
/// The stun itself is <see cref="TFStunStack.Stun"/> (stock's
/// <c>m_Shared.StunPlayer</c> call, tf_triggers.cpp:98), always the by-trigger
/// one with no attacker; the refusals (phase, interception, meg_heal, the
/// hidden invulnerable) are the stack's, as stock's are <c>m_Shared</c>'s. The
/// speed reduction waits on its consumer, the movement lane (deferred.tsv).
/// </para>
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerStun(IGameClock clock, IEngineEntities entities, TFPlayerCarriers carriers)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_stun";

    /// <summary>The <c>StunThink</c> re-arm (:115): stun the touchers again every half second.</summary>
    public const float StunInterval = 0.5f;

    [ScriptVars] public TriggerStunVars StunVars = new();

    public readonly Output OnStunPlayer = new();

    // m_flLastStunTime is stock's saved field (:137); nothing in this build
    // reads it, as stock's only reader is its own touch-link housekeeping.
    [SaveField] private float _lastStunTime;
    [SaveField] private bool _stunning;

    // m_stunEntities (DEFINE_UTLVECTOR, :38): who the last think round stunned,
    // so the EndTouch catch does not re-stun them.
    private readonly List<EntityHandle> _stunned = new();

    /// <summary><c>m_stunEntities</c>.</summary>
    public IReadOnlyList<EntityHandle> StunnedEntities => _stunned;

    /// <summary>Whether the stun think is armed (<c>m_pfnThink != NULL</c>).</summary>
    public bool Stunning => _stunning;

    /// <summary><c>Save</c> carries <c>m_stunEntities</c> as the utlvector.</summary>
    [SaveField("stun_entities")]
    private EntityHandle[] StunnedSaved
    {
        get => _stunned.ToArray();
        set
        {
            _stunned.Clear();
            foreach (EntityHandle h in value)
            {
                if (h != EntityHandle.None)
                    _stunned.Add(h);
            }
        }
    }

    /// <summary>
    /// <c>Touch</c> (:160-167): while the think is off, the first touch arms
    /// <c>StunThink</c> for <c>trigger_delay</c> from now.
    /// </summary>
    protected override void StartTouch(EntityHandle toucher)
    {
        if (!_stunning)
        {
            _stunning = true;
            SetNextThink(StunVars.TriggerDelay);
        }
    }

    /// <summary><c>StunThink</c> (:106-117): stun again while anyone is inside.</summary>
    public override void OnThink()
    {
        if (StunAllTouchers() <= 0)
            _stunning = false;
        else
            SetNextThink(StunInterval);
    }

    /// <summary>
    /// <c>EndTouch</c> (:119-133): a toucher the filters accept and the last
    /// think never stunned gets it on the way out.
    /// </summary>
    protected override void EndTouch(EntityHandle toucher)
    {
        if (!Passes(toucher) || _stunned.Contains(toucher))
            return;

        StunEntity(toucher);
    }

    /// <summary>
    /// <c>StunAllTouchers</c> (:135-158): the touch list emptied and re-stunned
    /// in one sweep; answers how many fell. Stock walks the engine's touch
    /// links; the volume's own inside-set is this build's touch list.
    /// </summary>
    public int StunAllTouchers()
    {
        if (Disabled)
            return 0;

        _lastStunTime = Clock.Now;
        _stunned.Clear();

        int stunCount = 0;
        foreach (EntityHandle toucher in Inside.ToList())
        {
            if (StunEntity(toucher))
                stunCount++;
        }

        return stunCount;
    }

    /// <summary>
    /// <c>StunEntity</c> (:66-104): <c>m_takedamage</c> (a player's is always
    /// on) and <c>PassesTriggerFilters</c> (the volume's <see cref="Passes"/>),
    /// then <c>ToTFPlayer</c> (the player's TF state), the type- and
    /// effects-built flags, the stun, the output and the tally.
    /// </summary>
    private bool StunEntity(EntityHandle other)
    {
        if (!entities.IsPlayer(other) || !Passes(other))
            return false;

        if (carriers.Part<TFStunStack>(other.EntryIndex) is not { } stuns)
            return false;

        var flags = TFStunFlags.Movement
                  | StunVars.StunType switch
                  {
                      // 0 movement only, 1 controls, 2 loser state (:76-89).
                      1 => TFStunFlags.Controls,
                      2 => TFStunFlags.LoserState,
                      _ => TFStunFlags.None,
                  };
        if (!StunVars.StunEffects)
            flags |= TFStunFlags.NoEffects;
        flags |= TFStunFlags.ByTrigger;

        stuns.Stun(other, StunVars.StunDuration, StunVars.MoveSpeedReduction, flags, attacker: EntityHandle.None);

        OnStunPlayer.Fire(); // m_OnStunPlayer.FireOutput( pOther, this ) (:100)
        _stunned.Add(other);

        return true;
    }
}

/// <summary>The map keys <see cref="TriggerStun"/> declares (tf_triggers.cpp:25-43's datadesc); stock's zeroed constructor leaves them all 0.</summary>
[ScriptVars]
public partial class TriggerStunVars
{
    /// <summary><c>m_flTriggerDelay</c>: seconds after the first touch that the stun think arms.</summary>
    [VarName("trigger_delay")] public float TriggerDelay { get; set; }

    /// <summary><c>m_flStunDuration</c>: seconds each stun lasts.</summary>
    [VarName("stun_duration")] public float StunDuration { get; set; }

    /// <summary><c>m_flMoveSpeedReduction</c>: the 0..1 share of the speed the stun takes.</summary>
    [VarName("move_speed_reduction")] public float MoveSpeedReduction { get; set; }

    /// <summary><c>m_iStunType</c>: 0 movement only, 1 controls, 2 loser state.</summary>
    [VarName("stun_type")] public int StunType { get; set; }

    /// <summary><c>m_bStunEffects</c>: keep the stun particles (stock's <c>stun_effects</c> integer).</summary>
    [VarName("stun_effects")] public bool StunEffects { get; set; }
}
