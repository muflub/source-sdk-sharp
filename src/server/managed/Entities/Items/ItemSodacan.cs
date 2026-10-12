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
/// The can an <see cref="EnvBeverage"/> drops. One mouthful of health, once,
/// and then it is gone.
///
/// Not a <see cref="PickupItem"/>, and the difference is worth stating: every
/// other item is placed by a mapper, is collectable the instant the map loads,
/// and comes back after twenty seconds. This one is created at runtime, spends
/// its first half second falling into the tray, and never returns — there is
/// nothing shared with the map items except the shell underneath.
///
/// What it owns is the half second. A can collected in mid-air is a can the
/// machine has not really dispensed yet, and the machine counts one can at a
/// time, so arming it early lets a player empty a full machine in one pass.
/// </summary>
[ScriptedEntity(EntityNames.ItemSodacan, ScriptedEntityKind.Item)]
public sealed class ItemSodacan : ScriptedEntity
{
    public ItemSodacan(IEngineEntityPhysics physics, IEnginePrecache precache, IEngineSound sound)
    {
        Physics = physics;
        Precache = precache;
        Sound = sound;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>making sounds, and what is heard.</summary>
    private readonly IEngineSound Sound;
    internal const string CanModel = "models/can.mdl";
    internal const string BounceSound = "ItemSoda.Bounce";

    /// <summary>How long it spends falling. Stock's half second, unchanged.</summary>
    private const float FallTime = 0.5f;

    /// <summary>A sip, which is what a can of soda is worth.</summary>
    private const int SipHealth = 1;
    private const int MaxHealth = 100;

    private bool _settled;
    private bool _taken;

    /// <summary>
    /// Whether walking into it would do anything. False while it is still
    /// falling and false once somebody has had it — a trigger reports every
    /// tick a player stands in it, so "already taken" has to be a state and
    /// not an assumption about being called once.
    /// </summary>
    internal bool Collectible => _settled && !_taken;

    public override void OnSpawn()
    {
        SetModel(CanModel);
        Precache.PrecacheSound(BounceSound);

        // It falls into the tray rather than appearing in it. The shell only
        // integrates an arc while something is already moving — a velocity of
        // zero reads as "not in flight" — so the can is nudged rather than
        // left at rest for gravity to find. A unit a second against six
        // hundred is a rounding error in the arc and the difference between
        // falling and hanging in the air.
        Physics.SetGravity(Handle, 1f);
        Entities.SetVelocity(Handle, new Vector(0f, 0f, -1f));

        SetNextThink(FallTime);
    }

    /// <summary>
    /// It has landed. The noise is the cue to the player that there is
    /// something to collect, which is why the can only becomes collectable
    /// here — the sound and the arming are the same event.
    /// </summary>
    public override void OnThink()
    {
        Sound.EntityEmitSound(Handle, BounceSound);

        // Stock's box: eight units around, eight tall, sitting on the ground
        // rather than centred on it.
        MakePickup(8f, 8f, 8f);

        _settled = true;
    }

    public override void OnTouch(EntityHandle toucher)
    {
        if (!Collectible)
            return;

        _taken = true;
        Entities.SetHealth(toucher, Sip(Entities.GetHealth(toucher)));

        // Gone whether or not it healed anybody. Stock consumes the can on a
        // full player too, and that is the right reading: the player picked it
        // up and drank it, which is not the same question as whether it did
        // them any good.
        Remove();
    }

    /// <summary>
    /// What a sip leaves a player on. Stock goes through TakeHealth, which is
    /// the same arithmetic with the engine's cap; this states the cap, because
    /// a mouthful of soda is not a route past a hundred.
    ///
    /// A player ALREADY over the cap is left where they are rather than clamped
    /// down to it. Charging suits and map-given health put people above a
    /// hundred all the time, and a can that took health away from them would be
    /// a very strange thing to walk into.
    /// </summary>
    internal static int Sip(int current)
        => current >= MaxHealth ? current : Math.Min(MaxHealth, current + SipHealth);
}
