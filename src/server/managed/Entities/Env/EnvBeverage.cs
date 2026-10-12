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
/// env_beverage: a vending machine that drops a can.
///
/// What it decides is how many cans it holds, which colour comes out, and
/// whether one is already waiting to be taken. The can itself is
/// <see cref="ItemSodacan"/>, which owns the falling and the drinking.
/// </summary>
[ScriptedEntity(EntityNames.EnvBeverage)]
public sealed class EnvBeverage : ScriptedEntity
{
    public EnvBeverage(IEngineHost host, IEnginePrecache precache, IEngineEntityRender render)
    {
        Host = host;
        Precache = precache;
        Render = render;
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    /// <summary>What a map may set on this entity. See <see cref="EnvBeverageVars"/>.</summary>
    [ScriptVars] public EnvBeverageVars Vars = new();

    private const int RandomBeverage = 6;
    private const int BeverageSkins = 6;

    /// <summary>
    /// The can currently waiting to be taken.
    ///
    /// Stock uses a BOOLEAN that the can clears on its way out —
    /// <c>CItemSoda::CanTouch</c> casts its owner to a CEnvBeverage and writes
    /// the field directly. That cannot survive the move: neither entity is a
    /// CBaseEntity any more, so there is no owner to cast and no field to write
    /// through. Left as stock has it, the machine would dispense once and stop.
    ///
    /// So the question is asked the other way round. Rather than being told the
    /// can has gone, this holds the can and asks whether it still exists — which
    /// needs nothing from the can at all, and answers correctly however it left.
    /// See docs/divergences.md.
    /// </summary>
    private EntityHandle _waiting = EntityHandle.None;

    public override void OnSpawn()
    {
        // The can's model and its landing noise, precached HERE because here is
        // inside the level-load window. The can precaches them too, but a can
        // only exists once somebody has used the machine — by which time the
        // map is long since built and a precache is a warning and silence.
        Precache.PrecacheModel(ItemSodacan.CanModel);
        Precache.PrecacheSound(ItemSodacan.BounceSound);

        // Stock's default, and it clamps rather than defaults: a machine with no
        // health key holds ten, and one written as zero is empty.
        if (Vars.Remaining == 0)
            Vars.Remaining = 10;
    }

    [ScriptedEvent("Activate")]
    private void OnActivate() => Dispense();

    [ScriptedEvent("Use")]
    private void OnUsed() => Dispense();

    private void Dispense()
    {
        if (Vars.Remaining <= 0)
            return;

        // One at a time. The handle answers this without the can having to
        // report in — a can that was taken, removed, or never made all read the
        // same way here, which is the point.
        if (Entities.IsValid(_waiting))
            return;

        var can = Entities.Create(EntityNames.ItemSodacan);

        if (!Entities.IsValid(can))
        {
            Log.Warning($"[SourceSharp] env_beverage '{Name}': could not create item_sodacan\n");
            return;
        }

        Entities.SetOrigin(can, Origin);
        Entities.SetAngles(can, Entities.GetAngles(Handle));

        // The owner is set because stock sets it, and it still does what the
        // rest of the engine uses an owner for — not colliding with the machine
        // that made it. What it no longer carries is the can's way of telling
        // the machine it was taken; see the field above.
        Entities.SetOwner(can, Handle);
        Entities.Spawn(can);

        // The skin is picked AFTER spawn: a model has to exist before one of its
        // skins can be chosen, and the shell applies a pending model during
        // spawn rather than before it.
        //
        // It currently lands nowhere. The can moved to the parallel hierarchy
        // with item_sodacan, and that hierarchy has no skin — the client's
        // GetSkin returns a hardcoded zero and nothing networks one. So every
        // can comes out in the first livery until the shell grows the field.
        // The choice is still made here, because it is still this entity's to
        // make and the day it is carried nothing here should have to change.
        // See docs/divergences.md.
        Render.SetSkin(can, SkinFor(Vars.BeverageType));

        _waiting = can;
        Vars.Remaining--;
    }

    /// <summary>
    /// Which livery to use. Six means random, which is stock's sentinel rather
    /// than a seventh colour.
    ///
    /// The draw comes from the ENGINE's stream, not a managed one: server
    /// randomness is replayed by demo playback and has to come out the same.
    /// </summary>
    internal int SkinFor(int beverageType)
        => beverageType == RandomBeverage
            ? (int)Host.RandomFloat(0f, BeverageSkins - 1)
            : beverageType;
}

/// <summary>
/// The map keys <see cref="EnvBeverage"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvBeverageVars
{
    /// <summary>Which livery. Six is "pick one at random", which is stock's.</summary>
    public int BeverageType { get; set; }

    /// <summary>
    /// How many cans are left. Stock keeps this in the entity's HEALTH, which is
    /// why an empty machine and a dead one are the same state to it.
    /// </summary>
    [VarName("health")]
    public int Remaining { get; set; }
}
