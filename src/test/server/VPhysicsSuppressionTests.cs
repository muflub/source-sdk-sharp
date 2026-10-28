using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// [SuppressGCTransition] on vphysics' calls (plan_physics_bones_perf B2),
/// which go through their forwarders (plan_engine_forwarders.md): the
/// suppression is on the forwarder table's field. SuppressionAuditTests reads
/// the native body of every suppressed shim slot; vphysics' bodies are
/// Valve's, outside this tree, so these methods are held to a reviewed list
/// instead, each checked against vphysics.so's behaviour to be a member or
/// IVP-core read that returns. A table suppresses exactly the reviewed methods
/// it forwards. A method added to the generator's suppress list without
/// review, or any suppressed one whose name says it changes something, goes
/// red here.
/// </summary>
public class VPhysicsSuppressionTests
{
    /// <summary>The reviewed methods, by forwarder table.</summary>
    private static readonly Dictionary<string, string[]> Reviewed = new()
    {
        ["PhysicsObjectFwd"] =
        [
            "IsStatic", "IsAsleep", "IsTrigger", "IsMoveable",
            "GetGameData", "GetGameFlags", "GetCallbackFlags", "GetContents",
            "GetMass", "GetInertia", "GetShadowController", "GetCollide",
            "GetPosition", "GetPositionMatrix", "GetVelocity", "GetVelocityAtPoint",
            "LocalToWorld", "WorldToLocal", "LocalToWorldVector", "WorldToLocalVector",
            "CalculateLinearDrag", "CalculateAngularDrag",
        ],
        ["PhysicsConstraintGroupFwd"] = ["IsInErrorState"],
    };

    /// <summary>
    /// A name that changes state or can reach managed: RecheckCollisionFilter
    /// asks the managed solver's ShouldCollide, Wake and the setters can fire
    /// object events, Create* allocates.
    /// </summary>
    private static readonly Regex Mutating = new(
        @"^(Set|Enable|Wake|Sleep|Recheck|Apply|Add|Create|Destroy|Become|Remove|Update|Teleport|Clear|Activate|Simulate|Release|Attach|Detach|Mark|Delete|Recompute|Next|Outward|Bounce)",
        RegexOptions.Compiled);

    // The vphysics forwarder tables both sides call (SharedForwarders.g.cs).
    private static IEnumerable<Type> VPhysicsLayouts()
        => typeof(Crossings).Assembly.GetTypes()
               .Where(t => t.IsValueType && t.Namespace == "SourceSharp.Abi"
                        && t.Name.StartsWith("Physics", StringComparison.Ordinal)
                        && t.Name.EndsWith("Fwd", StringComparison.Ordinal));

    private static IEnumerable<(string Name, Type Type)> Slots(Type table)
        => table.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => (f.Name, f.GetModifiedFieldType()));

    private static List<string> Suppressed(Type table)
        => Slots(table).Where(s => EngineApiAreasTests.IsSuppressed(s.Type)).Select(s => s.Name).ToList();

    [Fact]
    public void The_vphysics_layouts_are_found()
    {
        var names = VPhysicsLayouts().Select(t => t.Name).ToList();
        Assert.True(names.Contains("PhysicsObjectFwd") && names.Contains("PhysicsConstraintGroupFwd"),
                    $"the vphysics forwarder tables are found ({string.Join(", ", names)})");
    }

    [Fact]
    public void Each_layouts_suppressed_slots_are_exactly_the_reviewed_ones()
    {
        foreach (Type layout in VPhysicsLayouts())
        {
            // The reviewed methods this table forwards: a reviewed method
            // managed does not call has no slot to suppress.
            var forwarded = Slots(layout).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            string[] expected = Reviewed.TryGetValue(layout.Name, out string[]? r) ? r.Where(forwarded.Contains).ToArray() : [];
            Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal),
                         Suppressed(layout).OrderBy(s => s, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void No_suppressed_vphysics_slot_is_named_for_a_change()
    {
        var offenders = VPhysicsLayouts()
            .SelectMany(t => Suppressed(t).Where(n => Mutating.IsMatch(n)).Select(n => $"{t.Name}.{n}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"no suppressed vphysics slot changes state ({string.Join(", ", offenders)})");
    }

    [Fact]
    public void The_change_rule_sees_the_slots_it_names()
    {
        // The rule's positive arm: the unsuppressed setters and the recheck are
        // fields it would refuse, so a pass above is not a pattern matching nothing.
        Type layout = VPhysicsLayouts().Single(t => t.Name == "PhysicsObjectFwd");
        var named = Slots(layout)
                    .Select(s => s.Name)
                    .Where(n => Mutating.IsMatch(n))
                    .ToList();

        Assert.Contains("RecheckCollisionFilter", named);
        Assert.Contains("Wake", named);
        Assert.Contains("SetVelocity", named);
    }
}
