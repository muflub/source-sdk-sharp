using System.Reflection;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ScriptedEntity.MovesAsPusher"/> against stock: which brush
/// entities stock gives MOVETYPE_PUSH, as its ss_sandbox census measured them
/// (tools/stock-ab ab_movetypes, stock hl2mp at its stock-ab patch 0006,
/// 2026-10-25; <c>trigger</c> and <c>simple_physics_brush</c>, which the map
/// does not place, from their Spawns: NONE and VPHYSICS).
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons weapon_crossbow,
/// 2026-10-25): a bolt that glanced off the floor into buttons_wheel, a
/// momentary_rot_button, stuck in it as in a wall, where stock's took the
/// moving-entity arm (its impact, then gone) -- the shell reports every
/// managed brush as MOVETYPE_NONE.
/// </para>
/// </summary>
public class MovesAsPusherTests
{
    private static readonly HashSet<string> StockPushers = new(StringComparer.OrdinalIgnoreCase)
    {
        "func_breakable", "func_breakable_surf", "func_brush", "func_button", "func_clip_vphysics",
        "func_conveyor", "func_door", "func_door_rotating", "func_movelinear", "func_plat", "func_platrot",
        "func_reflective_glass", "func_rotating", "func_rot_button", "func_trackautochange",
        "func_trackchange", "func_tracktrain", "func_train", "func_vehicleclip", "func_wall",
        "func_wall_toggle", "func_water", "func_water_analog", "momentary_door", "momentary_rot_button",
    };

    private static readonly HashSet<string> StockNotPushers = new(StringComparer.OrdinalIgnoreCase)
    {
        "color_correction_volume", "env_bubbles", "env_embers", "func_areaportalwindow", "func_dustcloud",
        "func_dustmotes", "func_friction", "func_illusionary", "func_lod", "func_occluder", "func_physbox",
        "func_physbox_multiplayer", "func_precipitation", "func_pushable", "func_smokevolume",
        "func_traincontrols", "func_weight_button", "game_zone_player", "plat_trigger", "simple_physics_brush", "trigger",
        "trigger_apply_impulse", "trigger_brush", "trigger_cdaudio", "trigger_gravity", "trigger_hurt",
        "trigger_impact", "trigger_look", "trigger_multiple", "trigger_once", "trigger_playermovement",
        "trigger_proximity", "trigger_push", "trigger_remove", "trigger_serverragdoll", "trigger_soundscape",
        "trigger_teleport", "trigger_teleport_relative", "trigger_transition", "trigger_vphysics_motion",
        "trigger_wind",
    };

    /// <summary>Every brush class this server registers, with the type that answers for it.</summary>
    private static IEnumerable<(string ClassName, Type Type)> BrushClasses()
        => typeof(ScriptedEntity).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ScriptedEntity).IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<ScriptedEntityAttribute>().Select(a => (a, t)))
            .Where(p => p.a.Kind == ScriptedEntityKind.Brush)
            .Select(p => (p.a.ClassName, p.t));

    // The answer is a constant of its class, so an instance with no
    // constructor run reads it as a spawned one does.
    private static bool Answers(Type type)
        => ((ScriptedEntity)RuntimeHelpers.GetUninitializedObject(type)).MovesAsPusher;

    [Fact]
    public void EveryBrushClassIsOneStockWasMeasuredFor()
    {
        string[] unmeasured = BrushClasses().Select(c => c.ClassName)
            .Where(n => !StockPushers.Contains(n) && !StockNotPushers.Contains(n)).ToArray();

        Assert.True(unmeasured.Length == 0,
                    "brush classes with no stock movetype on record (measure them with ab_movetypes): " +
                    string.Join(", ", unmeasured));
    }

    [Fact]
    public void EveryBrushClassAnswersAsStocksMovetype()
    {
        var classes = BrushClasses().ToArray();
        Assert.True(classes.Length > 50, $"only {classes.Length} brush classes found");

        string[] wrong = classes.Where(c => Answers(c.Type) != StockPushers.Contains(c.ClassName))
                                .Select(c => $"{c.ClassName} ({c.Type.Name}) says {Answers(c.Type)}").ToArray();

        Assert.True(wrong.Length == 0, string.Join("; ", wrong));
    }

    /// <summary>The control: a class nothing overrides answers no.</summary>
    [Fact]
    public void APointEntityIsNoPusher()
        => Assert.False(Answers(typeof(ScriptedEntity).Assembly.GetTypes()
                                 .First(t => t.Name == "InfoTarget")));
}
