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
/// A plate that notices what is standing on it. Put enough weight on it and it
/// presses; take the weight away and it lets go.
///
/// The plate itself is scenery — it never moves, and the only reason it is in
/// the physics world at all is so that crates can rest on it rather than fall
/// through. Stock says the same thing with an immovable shadow object; the
/// difference is that stock then MEASURES that object, reading the normal force
/// at each of its contact points and summing them into what it calls "stress".
/// This weighs what is on the plate instead, which is the question the mapper's
/// keyvalue asks — <c>WeightToActivate</c> is a weight, not a force.
///
/// The two are the same quantity, and not merely a similar one.
/// <c>CalculateObjectStress</c> divides its force sum by the magnitude of
/// gravity on the way out and says so in its last comment, "sum is now kg". So
/// stock already answers in kilograms, and a sum of the masses resting on the
/// plate is what a threshold tuned against stock was tuned against.
/// </summary>
[ScriptedEntity(EntityNames.FuncWeightButton, ScriptedEntityKind.Brush)]
public sealed class FuncWeightButton : ScriptedEntity
{
    public FuncWeightButton(IEngineNetworking net, IEngineEntityPhysics physics, IEngineWorldQuery world)
    {
        Net = net;
        Physics = physics;
        World = world;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    public readonly Output OnPressed = new();
    public readonly Output OnReleased = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncWeightButtonVars"/>.</summary>
    [ScriptVars] public FuncWeightButtonVars Vars = new();

    /// <summary>
    /// How far above the plate's own top a load may begin and still be counted.
    ///
    /// A crate at rest settles a fraction of a unit into whatever holds it, and
    /// the plate's brush hull and its bounding box are not the same shape, so a
    /// contact test written to the unit would miss loads that are plainly
    /// sitting there.
    /// </summary>
    private const float Slack = 4f;

    /// <summary>
    /// How high above the plate the column of counted weight reaches.
    ///
    /// Stock gets stacking for free, because a normal force at the plate carries
    /// everything the bottom crate is holding up as well. Counting only what
    /// touches would lose that, and stacking is how a player answers a plate
    /// asking for more than one crate's worth — so the column is what is
    /// counted, bounded so that a walkway loaded with scenery two storeys up is
    /// not weighing on a plate in the floor.
    /// </summary>
    private const float StackReach = 128f;

    private bool _pressed;

    public override void OnSpawn()
    {
        // Solid and never simulated, which is the whole of what the plate is.
        // Without an object in the solver a brush is solid to a walking player
        // and invisible to vphysics, and every crate a mapper drops on the
        // plate falls straight through it.
        //
        // Static rather than stock's shadow: a shadow is for a brush that may
        // later be moved or constrained, and a weight button is neither.
        MakePhysicsStatic();

        // Every tick, as stock does. A plate that answers a tenth of a second
        // late is a plate that fires after the crate has bounced off it again.
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        // A plate with nothing in the solver can never be stood on, so weighing
        // it every tick forever would only be a radius query answering zero.
        // Stock stops for the same reason.
        if ((Net.EntityGetState(Handle, out _) & EntityState.Physics) == 0)
        {
            StopThinking();
            return;
        }

        SetNextThink(0f);
        Weigh(LoadOnPlate());
    }

    /// <summary>
    /// The latch, given the weight that is on the plate.
    ///
    /// Split out and taking the number so a test can drive it: all of the
    /// entity's behaviour is here, and none of it needs a crate to exist. Equal
    /// to the threshold is deliberately neither pressed nor released, which is
    /// stock's own pair of strict comparisons and matters for a plate keyed to
    /// exactly one crate's mass.
    /// </summary>
    internal void Weigh(float weight)
    {
        if (weight > Vars.WeightToActivate && !_pressed)
        {
            OnPressed.Fire();
            _pressed = true;
        }
        else if (weight < Vars.WeightToActivate && _pressed)
        {
            OnReleased.Fire();
            _pressed = false;
        }
    }

    /// <summary>
    /// What the plate is holding up, in kilograms.
    ///
    /// Only things the solver moves are weighed. A player standing on the plate
    /// is not, and neither is a door resting against it — stock reaches the same
    /// answer from the other direction, since a mover pressing on a shadow
    /// object contributes no stress to it.
    ///
    /// The plate's OWN mass is not in the total, where stock's is: a shadow
    /// object is neither moveable nor static, which is the one case where
    /// CalculateObjectStress adds the measured object's mass to its own answer.
    /// An empty stock plate therefore already weighs something, and an empty one
    /// here weighs nothing.
    /// </summary>
    private float LoadOnPlate()
    {
        (Vector mins, Vector maxs) = Entities.GetBounds(Handle);

        Vector origin = Origin;
        Vector low = origin + mins;
        Vector high = origin + maxs;

        Vector top = new((low.X + high.X) * 0.5f, (low.Y + high.Y) * 0.5f, high.Z);

        // Wide enough to reach the far corner of the plate and the top of the
        // column above it, because the sphere is only the coarse pass — what
        // actually counts is decided box against box below.
        float halfDiagonal = new Vector(high.X - low.X, high.Y - low.Y, 0f).Length * 0.5f;

        Span<EntityHandle> found = stackalloc EntityHandle[32];
        int count = World.EntitiesInSphere(top, halfDiagonal + StackReach, found);

        float weight = 0f;

        for (int i = 0; i < count; i++)
        {
            EntityHandle load = found[i];

            if (load == Handle)
                continue;

            Net.EntityGetState(load, out MoveType moveType);

            if (moveType != MoveType.VPhysics)
                continue;

            float mass = Physics.GetMass(load);

            // Zero means "no physics object to weigh", the same reading
            // filter_activator_mass_greater takes of it.
            if (mass <= 0f)
                continue;

            if (StandsOnPlate(load, low, high))
                weight += mass;
        }

        return weight;
    }

    /// <summary>
    /// Whether something is over the plate and above it — the box test the
    /// sphere query above cannot express.
    /// </summary>
    private bool StandsOnPlate(EntityHandle load, Vector low, Vector high)
    {
        (Vector mins, Vector maxs) = Entities.GetBounds(load);

        Vector at = Entities.GetOrigin(load);
        Vector loadLow = at + mins;
        Vector loadHigh = at + maxs;

        // Overlapping the footprint rather than centred in it: half a crate on
        // the edge of a plate is half a crate the plate is holding up.
        if (loadHigh.X < low.X || loadLow.X > high.X)
            return false;

        if (loadHigh.Y < low.Y || loadLow.Y > high.Y)
            return false;

        return loadLow.Z >= high.Z - Slack
            && loadLow.Z <= high.Z + StackReach;
    }
}

/// <summary>
/// The map keys <see cref="FuncWeightButton"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncWeightButtonVars
{
    /// <summary>Kilograms that have to be on the plate before it presses.</summary>
    public float WeightToActivate { get; set; }
}
