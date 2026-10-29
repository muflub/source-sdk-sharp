using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// [ClaimsKeys] is believed by the description, the var inventory and the
/// stock contract diff, so every key it names must really be taken by its
/// class: the registry's KeyValue claims it. Found 2026-10-28: logic_case's
/// Case01..16, point_template's Template01..16 and logic_script's
/// Group00..16 were read in OnKeyValue, where nothing that described the
/// class could see them, and the contract diff listed all 49 as gaps.
/// </summary>
public class ClaimsKeysTests
{
    [Fact]
    public void Every_claimed_key_is_taken_by_its_class()
    {
        var unclaimed = new List<string>();
        int checkedKeys = 0;
        uint next = 0x0011_0100u;

        foreach (Type type in typeof(ScriptedEntity).Assembly.GetTypes())
        {
            var families = type.GetCustomAttributes<ClaimsKeysAttribute>().ToArray();
            if (families.Length == 0)
                continue;

            foreach (var entity in type.GetCustomAttributes<ScriptedEntityAttribute>())
            {
                var registry = new ScriptedEntityRegistry(TestEngines.Server());
                registry.RegisterAll(typeof(ScriptedEntity).Assembly);
                // point_template's store, which the container does not otherwise hold.
                registry.Services = Services(out var engine, new TemplateStore(new RecordingLog()));
                engine.Strict = false;

                foreach (string key in families.SelectMany(f => f.Keys()))
                {
                    checkedKeys++;
                    if (!registry.KeyValue(new EntityHandle(next++), entity.ClassName, key, "x"))
                        unclaimed.Add($"{entity.ClassName} claims '{key}' and does not take it");
                }
            }
        }

        Assert.True(checkedKeys >= 49, $"only {checkedKeys} claimed key(s) were checked");
        Assert.Empty(unclaimed);
    }

    [Fact]
    public void The_description_lists_a_claimed_family()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ScriptedEntity).Assembly);
        registry.Services = Services();

        var logicCase = registry.DescribeClasses().Single(c => c.ClassName == "logic_case");

        Assert.Contains(logicCase.Vars, v => v.Name == "Case01");
        Assert.Contains(logicCase.Vars, v => v.Name == "Case16");
        Assert.DoesNotContain(logicCase.Vars, v => v.Name == "Case17");
    }
}
