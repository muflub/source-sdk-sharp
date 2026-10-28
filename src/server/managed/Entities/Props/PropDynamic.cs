//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Server;

/// <summary>
/// A model that is solid and can animate, but never moves on its own. The
/// scenery of a map: machinery, signage, doors that are only decoration.
///
/// The difference from prop_physics is not what it looks like but what the
/// engine does with it — this is collided with and never simulated, which is
/// what makes a map full of them affordable.
/// </summary>
[ScriptedEntity(EntityNames.PropDynamic, ScriptedEntityKind.Prop)]
// The same entity under the names the editor uses for it.
[ScriptedEntity(EntityNames.DynamicProp, ScriptedEntityKind.Prop)]
[ScriptedEntity(EntityNames.PropDynamicOverride, ScriptedEntityKind.Prop)]
public sealed class PropDynamic : ScriptedEntity
{
    public PropDynamic(IEnginePrecache precache, IEngineEntityRender render, IServerPropData propData,
                       IEngineHost host, ModelDescriptors models)
    {
        Precache = precache;
        Render = render;
        PropDataTable = propData;
        Host = host;
        Models = models;
    }

    private readonly IEnginePrecache Precache;
    private readonly IEngineEntityRender Render;
    private readonly IServerPropData PropDataTable;
    private readonly IEngineHost Host;
    private readonly ModelDescriptors Models;

    /// <summary>The sequence, rate and start the client runs. See <see cref="PropDynamicNetState"/>.</summary>
    [NetState] public PropDynamicNetState State = new();

    // m_iGoalSequence, m_iTransitionDirection, m_flNextRandAnim, and the
    // playback rate FinishSetSequence last set (CBaseAnimating's default 1).
    private int _goalSequence = -1;
    private int _transitionDirection;
    private float _nextRandAnim;
    private float _rate = 1f;

    public readonly Output OnAnimationBegun = new();
    public readonly Output OnAnimationDone = new();

    /// <summary>What a map may set on this entity. See <see cref="PropDynamicVars"/>.</summary>
    [ScriptVars] public PropDynamicVars Vars = new();

    private bool _visible = true;

    /// <summary>SF_DYNAMICPROP_NO_VPHYSICS.</summary>
    private const int NoVPhysicsFlag = 128;

    /// <summary>SF_DYNAMICPROP_DISABLE_COLLISION.</summary>
    private const int DisableCollisionFlag = 256;

    /// <summary>CBaseProp's MOVETYPE_PUSH (stock's ss_sandbox census measures 7).</summary>
    public override bool MovesAsPusher => true;

    /// <summary>CreateVPhysics makes a static object or none; never a simulated one.</summary>
    public override bool SpawnsPhysics => false;

    public override void OnSpawn()
    {
        // Condense classname's to one, except for "prop_dynamic_override" (CDynamicProp::Spawn).
        if (string.Equals(ClassName, EntityNames.DynamicProp, StringComparison.OrdinalIgnoreCase))
            SetClassName(EntityNames.PropDynamic);

        if (DynamicPropSpawn.Refusal(ClassName, Vars.Model, Precache, Render, PropDataTable) is { } refusal)
        {
            Log.Msg(DynamicPropSpawn.Line(ClassName, SpawnOrigin, refusal));
            Remove();
            return;
        }

        SetModel(Vars.Model);

        // The map's solid key (SOLID_NONE when it writes none). A prop_dynamic
        // left without one is an oriented box nothing collides with, "so the
        // bounding box surrounds the prop as it rotates"; the override and the
        // classes derived from it keep SOLID_NONE (CDynamicProp::Spawn).
        SolidType solid = (SolidType)Vars.Solid;
        SolidFlags flags = SolidFlags.None;
        if (solid == SolidType.None && ClassName is EntityNames.PropDynamic)
        {
            solid = SolidType.OrientedBox;
            flags = SolidFlags.NotSolid;
        }

        // CreateVPhysics: a static object (VPhysicsInitStatic) unless it is
        // SOLID_NONE, or not solid and asked for none. The object keeps the
        // solid type: a brush's SetSolidity would make it SOLID_BSP, which a
        // studio model is not traced as. The collision spawnflag comes after.
        bool noObject = (flags & SolidFlags.NotSolid) != 0 && (SpawnFlags & NoVPhysicsFlag) != 0;

        if ((SpawnFlags & DisableCollisionFlag) != 0)
            flags |= SolidFlags.NotSolid;

        SetSolidity(solid, flags);

        if (solid != SolidType.None && !noObject)
            InitStaticPhysics();

        // Now condense all classnames to one: the override too, once the
        // spawn has read it (CDynamicProp::Spawn after BaseClass::Spawn).
        if (string.Equals(ClassName, EntityNames.PropDynamicOverride, StringComparison.OrdinalIgnoreCase))
            SetClassName(EntityNames.PropDynamic);

        _visible = !Vars.StartDisabled;
        Apply();

        if (Vars.RandomAnimation || Vars.DefaultAnim.Length > 0)
        {
            if (Vars.RandomAnimation)
            {
                _nextRandAnim = GameTime + Host.RandomFloat(Vars.MinAnimTime, Vars.MaxAnimTime);

                // Stock books curtime + m_flNextRandAnim + 0.1, and m_flNextRandAnim
                // is already a time: the first think comes a whole game time late.
                SetNextThink(_nextRandAnim + 0.1f);
            }
            else
            {
                PropSetAnim(Vars.DefaultAnim);
            }
        }
    }

    [ScriptedEvent] private void OnTurnOn() { _visible = true; Apply(); }
    [ScriptedEvent] private void OnTurnOff() { _visible = false; Apply(); }
    [ScriptedEvent] private void OnEnable() { _visible = true; Apply(); }
    [ScriptedEvent] private void OnDisable() { _visible = false; Apply(); }

    [ScriptedEvent] private void OnEnableCollision() => SetNotSolid(false);
    [ScriptedEvent] private void OnDisableCollision() => SetNotSolid(true);

    [ScriptedEvent] private void OnSetAnimation(string animation) => PropSetAnim(animation);

    [ScriptedEvent] private void OnSetDefaultAnimation(string animation) => Vars.DefaultAnim = animation;

    /// <summary>
    /// AnimThink: a random animator picks a new idle when its time comes; a
    /// transition that has played out moves on towards the goal sequence, and
    /// the goal played out fires OnAnimationDone and returns to the default
    /// animation. Otherwise it looks again in a tenth of a second.
    /// </summary>
    public override void OnThink()
    {
        if (Vars.RandomAnimation && _nextRandAnim < GameTime)
        {
            ModelDescriptor model = Models.For(Handle);
            Render.SequenceState(Handle, out int current, out _, out _, out _, out _);
            int idle = SequenceGraph.SelectWeightedSequence(model.Sequences, "ACT_IDLE", current, Host.RandomInt);

            // ResetSequence and ResetClientsideFrame: from its first frame on both sides.
            Start(idle < 0 ? 0 : idle, _rate, 0f);
            OnAnimationBegun.Fire();

            _nextRandAnim = GameTime + Host.RandomFloat(Vars.MinAnimTime, Vars.MaxAnimTime);
        }

        Render.SequenceState(Handle, out int sequence, out float cycle, out _, out bool looping, out _);
        if (((_transitionDirection > 0 && cycle >= 0.999f) || (_transitionDirection < 0 && cycle <= 0f)) && !looping)
        {
            if (sequence != _goalSequence)
            {
                PropSetSequence(_goalSequence);
            }
            else
            {
                OnAnimationDone.Fire();

                // If I'm a random animator, think again when it's time to change
                // sequence (the same time-for-a-delay as the spawn's).
                if (Vars.RandomAnimation)
                    SetNextThink(_nextRandAnim + 0.1f);
                else if (Vars.DefaultAnim.Length > 0)
                    PropSetAnim(Vars.DefaultAnim);
            }
        }
        else
        {
            SetNextThink(0.1f);
        }
    }

    /// <summary>
    /// PropSetAnim: a sequence by name, walked to through the model's graph,
    /// with OnAnimationBegun. A name the model does not have is said out loud
    /// and leaves it on its first sequence: the symptom otherwise is scenery
    /// frozen in its first frame, which looks like the entity failing.
    /// </summary>
    private void PropSetAnim(string animation)
    {
        int sequence = Render.LookupSequence(Handle, animation);
        if (sequence > -1)
        {
            PropSetSequence(sequence);
            OnAnimationBegun.Fire();
            return;
        }

        Log.Warning($"[SourceSharp] Dynamic prop {Name}: no sequence named:{animation}\n");
        Start(0, _rate, 0f);
    }

    /// <summary>
    /// PropSetSequence: the goal is set, the first step towards it starts
    /// (GotoSequence; nothing while a transition is still playing), and the
    /// think that watches it is booked unless one already is.
    /// </summary>
    private void PropSetSequence(int sequence)
    {
        _goalSequence = sequence;

        Render.SequenceState(Handle, out int current, out float cycle, out _, out _, out _);
        if (SequenceGraph.GotoSequence(Models.For(Handle), current, cycle, _rate, _goalSequence) is { } step)
        {
            _transitionDirection = step.Direction;
            FinishSetSequence(step.Sequence);
        }

        if (Thinks.NextThinkTime(Handle, null) <= GameTime)
            SetNextThink(0.1f);
    }

    /// <summary>FinishSetSequence: forwards from 0, or backwards from 0.999.</summary>
    private void FinishSetSequence(int sequence)
        => Start(sequence, _transitionDirection > 0 ? 1f : -1f, _transitionDirection > 0 ? 0f : 0.999f);

    /// <summary>A sequence started over, on the server's skeleton and in what the client runs.</summary>
    private void Start(int sequence, float rate, float cycle)
    {
        _rate = rate;
        Render.PlaySequenceAt(Handle, sequence, rate, cycle);

        State.Sequence = sequence;
        State.PlaybackRate = rate;
        State.StartCycle = cycle;
        State.StartTime = GameTime;
        State.Starts++;
    }

    /// <summary>
    /// TurnOn and TurnOff: EF_NODRAW alone. Stock's hidden prop_dynamic is
    /// still solid; DisableCollision is the input that lets things through.
    /// </summary>
    private void Apply() => SetVisible(_visible);
}

/// <summary>
/// The map keys <see cref="PropDynamic"/> declares.
/// </summary>
[ScriptVars]
public partial class PropDynamicVars
{
    public string Model { get; set; } = string.Empty;

    /// <summary>Animation to play on spawn and return to when another finishes.</summary>
    public string DefaultAnim { get; set; } = string.Empty;

    /// <summary>Pick a weighted ACT_IDLE sequence now and then, instead of the default animation.</summary>
    public bool RandomAnimation { get; set; }

    /// <summary>The shortest wait between random picks, in seconds.</summary>
    public float MinAnimTime { get; set; }

    /// <summary>The longest wait between random picks, in seconds.</summary>
    public float MaxAnimTime { get; set; }

    public bool StartDisabled { get; set; }

    /// <summary>The map's solid type: 0 none (the default), 2 a box, 6 the model's collision.</summary>
    public int Solid { get; set; }
}
