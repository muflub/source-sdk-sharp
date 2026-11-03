using System.Reflection;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// Who in the HL2MP binding may read another actor (plan_hl2mp_ai §4.4): an IL
/// census over SourceSharp.Ai.Hl2mp of every call that reads where a player
/// or entity is, what it holds, or its health, clip or reserve. Only
/// <see cref="VisionPass"/> and <see cref="SnapshotBuilder"/> read other
/// actors; the other callers are named with what they read (the bot itself,
/// a door, a debug command's target), so a new reader is red until it is
/// argued for here. And <see cref="SnapshotBuilder"/> never calls a read of
/// health, armour, clip or reserve: a bot's view of another player cannot
/// carry them.
/// </summary>
public sealed class PerceptionCensusTests
{
    private static readonly Assembly Binding = typeof(VisionPass).Assembly;

    /// <summary>The reads of an actor's state the census watches, by declaring type and member.</summary>
    private static readonly string[] Reads =
    [
        "IEngineEntities::GetOrigin", "IEngineEntities::GetEyePosition", "IEngineEntities::GetVelocity", "IEngineEntities::GetEyeAngles",
        "IEngineEntities::GetWorldSpaceCenter", "IEngineEntities::GetHealth", "IEngineEntities::GetArmor", "IEngineEntities::GetAngles",
        "IEnginePlayers::GetActiveWeapon", "IEnginePlayers::GetActiveWeaponEntity", "IEnginePlayers::GetWeaponEntity", "IEnginePlayers::GetShootOrigin",
        "Arsenal::Holdings", "Arsenal::CarriedClipOf", "Arsenal::PoolOf", "Arsenal::Pools", "Arsenal::MaxCarryOf",
    ];

    /// <summary>The binding's readers, each with what it reads.</summary>
    private static readonly string[] Readers =
    [
        // The two perception types (§4.4).
        typeof(VisionPass).FullName!, typeof(SnapshotBuilder).FullName!,
        // The bot itself: its own origin, eye, view and holdings (§2.4), its own HUD (§4.3).
        typeof(EngineBotSelf).FullName!, typeof(ArsenalBotInventory).FullName!, typeof(EngineBotHud).FullName!,
        // Doors and props the nav blocks, never a player (§6.3).
        "SourceSharp.Ai.Hl2mp.Nav.EngineObstacleEyes",
        // The solid entities' cells (ruling 2026-11-02), never a player, a weapon or an item: the
        // field reads the truth and gives a bot only what was in its PVS (NavObstacleField).
        "SourceSharp.Ai.Hl2mp.Nav.EngineObstacleWorld",
        // ai_bot_goto: the debug command's target, an item by class or a point.
        "SourceSharp.Ai.Hl2mp.AiBotGotoCommand",
        // The inspector's perception part (§7.1): the bot's own view, for the web UI only.
        typeof(AiBotRoutes).FullName!,
        // The recording's hit: the weapon in the attacker's hands, written to the file, never to a brain (§3.4.4).
        typeof(ShotTap).FullName!,
        // The 3D view's route (§7.1): the bot's own view too, for the web UI only; held below.
        typeof(AiWorldRoutes).FullName!,
    ];

    private static bool IsRead(MethodBase m) => Reads.Contains($"{m.DeclaringType?.Name}::{m.Name}");

    private static bool IsEntityDirectory(MethodBase m) => m.DeclaringType?.Name == "IEntityDirectory";

    [Fact]
    public void OnlyTheNamedTypesReadAnActorsState()
    {
        string[] callers = IlCallCensus.Callers(IsRead, Binding);
        Assert.True(Readers.Order(StringComparer.Ordinal).SequenceEqual(callers), "the readers: " + string.Join(", ", callers));
    }

    /// <summary>
    /// A route is outside the bots' view (§7.1): the 3D view's truth reaches
    /// nothing but the route provider, and its heard log, which only the tap
    /// fills, nothing but the route.
    /// </summary>
    [Fact]
    public void NoBrainReadsTheWorldRouteOrTheHeardLog()
    {
        string route = typeof(AiWorldRoutes).FullName!;
        string[] routeCallers = [.. IlCallCensus.Callers(m => m.DeclaringType == typeof(AiWorldRoutes), Binding).Where(c => c != route)];
        Assert.Equal(["SourceSharp.Ai.Hl2mp.Nav.AiDebugRoutes"], routeCallers);
        string[] logCallers = [.. IlCallCensus.Callers(m => m.DeclaringType == typeof(HeardLog), Binding).Where(c => c != typeof(HeardLog).FullName)];
        Assert.Equal(new[] { route, typeof(SoundTap).FullName! }.Order(StringComparer.Ordinal), logCallers);
    }

    /// <summary>
    /// The obstacle field reads the world's truth so a bot need not (ruling 2026-11-02): only
    /// it asks the world's seam, and what a bot routes by reaches it through
    /// <see cref="SourceSharp.Ai.Hl2mp.Nav.IObstacleSight"/>, the PVS a client would be sent an entity by.
    /// </summary>
    [Fact]
    public void OnlyTheObstacleFieldAsksTheObstacleWorld()
    {
        string[] callers = IlCallCensus.Callers(m => m.DeclaringType == typeof(SourceSharp.Ai.Hl2mp.Nav.IObstacleWorld), Binding);
        Assert.Equal(["SourceSharp.Ai.Hl2mp.Nav.NavObstacleField"], callers);
        Assert.Equal(["SourceSharp.Ai.Hl2mp.Nav.NavObstacleField"], IlCallCensus.Callers(m => m.DeclaringType == typeof(SourceSharp.Ai.Hl2mp.Nav.IObstacleSight), Binding));
    }

    /// <summary>A player, a weapon or an item stops nobody by our collision rules, and is not a thing the field may read.</summary>
    [Fact]
    public void TheObstacleWorldsRostersNameNoPlayerWeaponOrItem()
    {
        var world = new SourceSharp.Ai.Hl2mp.Nav.EngineObstacleWorld(null!);
        string[] classes = [.. Enumerable.Range(0, world.RosterCount).Select(SourceSharp.Ai.Hl2mp.Nav.EngineObstacleWorld.RosterClass)];
        Assert.Equal(world.RosterCount, classes.Distinct().Count());
        Assert.Contains("prop_physics", classes);
        Assert.Contains("func_brush", classes);
        Assert.DoesNotContain(classes, c => c == "player" || c.StartsWith("weapon_", StringComparison.Ordinal) || c.StartsWith("item_", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingInTheBindingCallsTheEntityDirectory()
        => Assert.Empty(IlCallCensus.Callers(IsEntityDirectory, Binding));

    [Fact]
    public void TheSnapshotBuilderNeverReadsHealthArmourClipOrReserve()
    {
        string[] calls = [.. IlCallCensus.Calls(m => m.DeclaringType?.Assembly != Binding, Binding)
                                         .Where(c => c.Caller == typeof(SnapshotBuilder).FullName).Select(c => c.Callee).Distinct()];
        Assert.True(calls.Contains("GetActiveWeapon"), "the control: the census sees the builder's calls");
        string[] truth = ["GetHealth", "GetArmor", "Holdings", "CarriedClipOf", "PoolOf", "Pools", "MaxCarryOf", "GetWeaponEntity"];
        Assert.Empty(calls.Intersect(truth));
    }
}
