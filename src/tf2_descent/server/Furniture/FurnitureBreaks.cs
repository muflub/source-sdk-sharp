using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Skills;
using TF2.Server.Player;
using TF2.Shared.Conditions;

namespace TF2.Descent.Server.Furniture;

/// <summary>
/// What breaking a crate or a barrel asks of the world beyond <see cref="IFurnitureWorld"/>:
/// TF2's wood break (the clients' pieces), a cloud's status on a hero and its look, and the
/// robots an ambush wakes. A seam of its own, so the furniture's facts hold the real service.
/// </summary>
public interface IFurnitureBreakWorld
{
    /// <summary>The gibs of every breakable model, precached while the level loads (each model's own and its pieces).</summary>
    void Precache(IEnumerable<string> models);

    /// <summary>
    /// <paramref name="model"/> breaks at <paramref name="origin"/>, facing <paramref name="yaw"/>:
    /// the clients in earshot each break their own copy into its pieces (stock's multiplayer
    /// <c>te-&gt;PhysicsProp</c> with the breakmodel flag), as a <c>prop_physics</c> of it would,
    /// the pieces leaving at <paramref name="velocity"/> (and each its burst away from the break).
    /// </summary>
    void Shatter(string model, Vector origin, float yaw, Vector velocity);

    /// <summary>The condition word's TF condition (<see cref="SkillConditions.OnPlayer"/>) on <paramref name="hero"/> for <paramref name="seconds"/>; false when it has none or TF refused it.</summary>
    bool Mark(EntityHandle hero, string condition, double seconds, EntityHandle provider);

    /// <summary>A cloud's splash at <paramref name="origin"/>: its particle and its sound.</summary>
    void Cloud(Vector origin);

    /// <summary>
    /// The robots of <paramref name="templates"/> woken beside <paramref name="origin"/> through the
    /// horde (each its own new roster row, lent a body), each hunting <paramref name="hero"/> for
    /// <paramref name="huntSeconds"/> when there is one; a line for each, said or refused.
    /// </summary>
    IReadOnlyList<string> Ambush(IReadOnlyList<string> templates, Vector origin, float yaw, EntityHandle hero, ulong seed, double huntSeconds);
}

/// <summary>The engine's temp entities and precache, TF2's conditions, and the horde's hand spawn and taunt.</summary>
[SideService(Side.Server, As = [typeof(IFurnitureBreakWorld)])]
public sealed class EngineFurnitureBreakWorld(ITempEnts temps, IEnginePrecache precache, IEngineWorldQuery world, ITFPlayerConditions conditions,
                                              HordeService horde, IHordeClock clock, IRobotStatusSink status) : IFurnitureBreakWorld
{
    /// <summary>The cloud's splash, as Jarate's (TF2's <c>peejar_impact</c>).</summary>
    public const string CloudParticle = "peejar_impact";

    public const string CloudSound = "Jar.Explode";

    /// <summary>How far from the break each ambusher stands, round it.</summary>
    public const float AmbushSpread = 48f;

    public void Precache(IEnumerable<string> models)
    {
        foreach (string model in models)
        {
            int pieces = precache.BreakPieceCount(model);   // precaches the model itself
            for (int i = 0; i < pieces; i++)
                if (precache.BreakPieceAt(model, i, 0f, CollisionGroup.None, out string piece, out _) && piece.Length > 0)
                    precache.PrecacheModel(piece);
        }
    }

    public void Shatter(string model, Vector origin, float yaw, Vector velocity)
    {
        const int BreakModelFlag = 1;
        temps.PhysicsProp(TeFilters.Pas(origin), 0f, precache.PrecacheModel(model), 0, origin, new QAngle(0f, yaw, 0f), velocity, BreakModelFlag, 0);
    }

    public bool Mark(EntityHandle hero, string condition, double seconds, EntityHandle provider) =>
        SkillConditions.OnPlayer(condition) is { } cond
        && conditions.Add(hero, cond, (float)seconds, provider) is TFCondAdd.Added or TFCondAdd.Refreshed or TFCondAdd.Merged;

    public void Cloud(Vector origin)
    {
        TeEffectData record = new() { Scale = 1f, HitBox = precache.ParticleSystemIndex(CloudParticle), Origin = origin, Start = origin };
        temps.DispatchEffect(TeFilters.Pvs(origin), 0f, "ParticleEffect", in record);
    }

    public IReadOnlyList<string> Ambush(IReadOnlyList<string> templates, Vector origin, float yaw, EntityHandle hero, ulong seed, double huntSeconds)
    {
        var lines = new List<string>();
        if (horde.Current is not { Begun: true } c)
        {
            lines.Add("no horde this level");
            return lines;
        }

        var lift = new Vector(0f, 0f, 16f);
        for (int i = 0; i < templates.Count; i++)
        {
            // Round the break, each clear of the walls by a line from where it stood, then down to the floor.
            float a = (yaw + 90f + 360f * i / Math.Max(1, templates.Count)) * MathF.PI / 180f;
            var dir = new Vector(MathF.Cos(a), MathF.Sin(a), 0f);
            var reach = world.TraceLine(origin + lift, origin + lift + dir * AmbushSpread, TraceMask.Brushes);
            var at = reach.Hit ? reach.End - dir * 16f : reach.End;
            var down = world.TraceLine(at, at - new Vector(0f, 0f, 256f), TraceMask.Brushes);
            var id = c.SpawnByHand(templates[i], null, HordeVectors.ToAi(down.End), yaw, seed + (ulong)i, clock.Tick, out string why);
            if (!id.IsValid)
            {
                lines.Add($"{templates[i]} refused: {why}");
                continue;
            }

            string hunt = "";
            if (hero != EntityHandle.None && c.Roster!.Body(id.Index) is int body and > 0)
                hunt = $", hunts the hero: {status.Threaten(c.Bodies!.Body(body).Handle, hero, huntSeconds, "breakable")}";
            lines.Add($"{templates[i]} as {id}{hunt}");
        }

        return lines;
    }
}
