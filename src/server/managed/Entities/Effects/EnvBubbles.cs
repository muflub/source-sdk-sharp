//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_bubbles: a volume of water that fizzes.
///
/// <para>
/// A BRUSH rather than a point, which is the whole of why it is not shaped like
/// the other emitters here: the mapper draws the volume the bubbles rise out of,
/// and the entity's job is to be invisible, be intangible, and emit into its own
/// shape on a timer.
/// </para>
///
/// <para>
/// The emission is a temp entity, and it is NOT the one stock uses. TE_Fizz
/// networks an entity index that the client resolves through
/// <c>cl_entitylist-&gt;GetEnt</c>, which answers a C_BaseEntity — NULL for
/// everything on this hierarchy — so stock's effect would arrive, find nothing
/// and silently emit no bubbles. TE_Bubbles carries the corners on the wire
/// instead. See <c>BubbleEffect</c> in sourcesharp.h and docs/divergences.md.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvBubbles, ScriptedEntityKind.Brush)]
public sealed class EnvBubbles : ScriptedEntity
{
    public EnvBubbles(IEngineEffects effects)
    {
        Effects = effects;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;
    /// <summary>What a map may set on this entity. See <see cref="EnvBubblesVars"/>.</summary>
    [ScriptVars] public EnvBubblesVars Vars = new();

    private const int StartOffFlag = 1;

    /// <summary>
    /// The first burst waits longer than the rest.
    ///
    /// Stock's two seconds, against a tenth of a second when switched on by an
    /// input. Kept because the difference is deliberate: a map full of these
    /// starting at once on level load would be a wall of temp entities in the
    /// first frame, and the entity that is turned on later is turned on because
    /// somebody is watching.
    /// </summary>
    private const float FirstDelay = 2f;

    private const float SwitchOnDelay = 0.1f;

    /// <summary>
    /// Whether the bubbles are flowing. Decided in <see cref="OnSpawn"/> and by
    /// the on/off inputs, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried it would come back false and an emitter
    /// that was running would go silent on reload
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _on;

    public override void OnSpawn()
    {
        // Invisible and intangible. The brush is the SHAPE the bubbles come out
        // of, not a thing in the world — stock sets alpha to zero and solidity
        // to none for exactly that, and a brush shell defaults to solid.
        SetVisible(false);
        SetSolidNone();

        if ((SpawnFlags & StartOffFlag) != 0)
            return;

        _on = true;
        SetNextThink(FirstDelay);
    }

    public override void OnThink()
    {
        if (!_on)
            return;

        Effects.BubbleEffect(Handle, Vars.Density, Vars.Current);
        SetNextThink(IntervalFor(Vars.Frequency));
    }

    /// <summary>
    /// Seconds between bursts, for a given "frequency".
    ///
    /// <para>
    /// The name lies twice. It is a PERIOD rather than a frequency — a bigger
    /// number is more often, but by subtraction rather than by division — and
    /// the scale has a cliff in it: from 0 to 19 the wait falls linearly from
    /// 2.5 seconds to 0.6, and at 20 it jumps to a flat half second and stays
    /// there however large the number gets.
    /// </para>
    /// <para>
    /// Kept exactly, cliff included. It is what every map with an env_bubbles
    /// was tuned against, and the discontinuity is invisible in play: 19 and 20
    /// are a tenth of a second apart.
    /// </para>
    /// <para>
    /// Static and taking the frequency so it can be checked without an engine.
    /// </para>
    /// </summary>
    internal static float IntervalFor(int frequency)
        => frequency > 19 ? 0.5f : 2.5f - (0.1f * frequency);

    /// <summary>Test-visible: the keys as the entity actually holds them.</summary>
    internal int Density => Vars.Density;

    internal float Current => Vars.Current;

    [ScriptedEvent("Activate")]
    private void OnActivate()
    {
        _on = true;
        SetNextThink(SwitchOnDelay);
    }

    [ScriptedEvent("Deactivate")]
    private void OnDeactivate()
    {
        _on = false;
        StopThinking();
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (_on) OnDeactivate(); else OnActivate();
    }

    [ScriptedEvent("SetCurrent")]
    private void OnSetCurrent(InputData input) => Vars.Current = input.Number;

    [ScriptedEvent("SetDensity")]
    private void OnSetDensity(InputData input) => Vars.Density = (int)input.Number;

    /// <summary>
    /// Change the rate, and RE-TIME the next burst rather than letting the one
    /// already scheduled run at the old rate.
    ///
    /// <para>
    /// Stock does the same, and only when running — which is the part worth
    /// keeping: rescheduling a stopped entity would start it, and a map that
    /// sets the frequency of something it has switched off means to set it, not
    /// to switch it on.
    /// </para>
    /// </summary>
    [ScriptedEvent("SetFrequency")]
    private void OnSetFrequency(InputData input)
    {
        Vars.Frequency = (int)input.Number;

        if (_on)
            SetNextThink(IntervalFor(Vars.Frequency));
    }
}

/// <summary>
/// The map keys <see cref="EnvBubbles"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvBubblesVars
{
    /// <summary>
    /// How fast the water is moving, which drags the bubbles sideways as they
    /// rise. The map's key is "current" and stock stores it in m_flSpeed.
    /// </summary>
    public float Current { get; set; }

    /// <summary>How many bubbles per burst.</summary>
    public int Density { get; set; }

    /// <summary>
    /// How OFTEN, on a scale that is not a frequency and not seconds.
    ///
    /// See <see cref="IntervalFor"/> — the arithmetic behind this key is the one
    /// genuinely surprising thing about the entity.
    /// </summary>
    public int Frequency { get; set; }
}
