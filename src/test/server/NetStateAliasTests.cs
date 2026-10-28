using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A networked state is declared under every classname its entity spawns as.
/// Found 2026-10-27: prop_dynamic_override and dynamic_prop share PropDynamic
/// and its PropDynamicNetState (508 bytes), but only prop_dynamic declared it.
/// The other two were published stateless and allocated at the smallest tier
/// (32 bytes), so binding the state refused the entity. On ss_sandbox, whose
/// chair is a prop_dynamic_override, that threw out of OnMapLoad and the map
/// stopped spawning at 106 of its 1267 entities.
/// </summary>
public class NetStateAliasTests
{
    [Fact]
    public void An_alias_is_a_declaration_of_its_own_with_the_same_layout()
    {
        var declared = StandaloneNetStates.Discover(typeof(PropDynamicNetState).Assembly, new RecordingLog())
                                          .Where(s => s.Type == typeof(PropDynamicNetState))
                                          .ToArray();

        Assert.Equal(new[] { EntityNames.DynamicProp, EntityNames.PropDynamic, EntityNames.PropDynamicOverride },
                     declared.Select(s => s.ClassName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Single(declared.Select(s => (s.Size, s.Fields.Count)).Distinct());
    }

    /// <summary>
    /// Every [ScriptedEntity] name of a class that holds a [NetState] is
    /// declared by one of the states it holds, by the state's own name or an
    /// alias. env_sprite's class holds one state per name and passes; a class
    /// spawned under a name none of its states declares fails.
    /// </summary>
    [Fact]
    public void Every_name_a_networked_entity_class_spawns_as_is_declared_by_one_of_its_states()
    {
        var declared = StandaloneNetStates.Discover(typeof(PropDynamicNetState).Assembly, new RecordingLog())
                                          .ToLookup(s => s.Type);
        var missing = new List<string>();
        int checkedNames = 0;

        foreach (Type type in typeof(ScriptedEntity).Assembly.GetTypes())
        {
            var names = type.GetCustomAttributes<ScriptedEntityAttribute>().Select(a => a.ClassName).ToArray();
            if (names.Length == 0)
                continue;

            var stateTypes = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 .Where(f => f.GetCustomAttribute<NetStateAttribute>() is not null)
                                 .Select(f => f.FieldType)
                                 .ToArray();
            if (stateTypes.Length == 0)
                continue;

            var covered = new HashSet<string>(stateTypes.SelectMany(t => declared[t]).Select(s => s.ClassName),
                                              StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                checkedNames++;
                if (!covered.Contains(name))
                    missing.Add($"{type.Name} spawns as '{name}', which none of its states declares");
            }
        }

        Assert.True(checkedNames > 10, $"only {checkedNames} name(s) were checked");
        Assert.Empty(missing);
    }
}
