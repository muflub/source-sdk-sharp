using System;
using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A shell-less class: kind <see cref="ScriptedEntityKind.Logical"/>, which is
/// the DEFAULT and is spelled out here so a reader does not have to know that.
/// </summary>
[ScriptedEntity("test_shellless_relay", ScriptedEntityKind.Logical)]
public sealed class TestShellLessRelay : ScriptedEntity
{
    public readonly Output OnTrigger = new();

    public readonly List<string> Inputs = new();
    public readonly List<EntityHandle> Activators = new();

    /// <summary>What <c>spawnflags</c> read at the moment <c>OnSpawn</c> ran.</summary>
    public int FlagsAtSpawn { get; private set; } = -1;

    /// <summary>Where the entity was at the moment <c>OnSpawn</c> ran.</summary>
    public Vector OriginAtSpawn { get; private set; }

    public int SpawnCount { get; private set; }

    public override void OnSpawn()
    {
        SpawnCount++;
        FlagsAtSpawn = SpawnFlags;
        // Through the entity's bound engine area, not EntityHandle's static
        // convenience: the area is the registry's side's (plan_abi_hotreload D7).
        OriginAtSpawn = Entities.GetOrigin(Handle);
    }

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        Inputs.Add(name);
        Activators.Add(activator);
        return true;
    }
}
