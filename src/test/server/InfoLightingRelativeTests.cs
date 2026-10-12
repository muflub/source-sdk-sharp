using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// info_lighting_relative, which is a marker with one job it can silently not
/// do.
///
/// <para>
/// The landmark is resolved BY NAME and at THINK time, and both halves of that
/// can go wrong without a symptom. Resolving at spawn finds nothing, because the
/// map is still being read when this entity spawns — and "found nothing" and
/// "was never asked for" are the same observable state, an invalid handle, so a
/// lookup moved back to spawn would look exactly like a landmark that does not
/// exist. The other half is the warning: a mapper who mistypes a landmark name
/// gets told by stock, and a port that resolves quietly takes that away.
/// </para>
/// </summary>
public class InfoLightingRelativeTests
{
    private static readonly EntityHandle Ent = new(0x005B_0001u);
    private static readonly EntityHandle Landmark = new(0x005B_0002u);

    /// <summary>
    /// Spawns one, capturing what it logs. The engine double is strict, so the
    /// name lookup has to be stubbed with the answer the test wants.
    /// </summary>
    private static (ScriptedEntityRegistry Registry, InfoLightingRelative Ent, List<string> Warnings)
        SpawnWith(EntityHandle answer, params (string Key, string Value)[] keys)
    {
        var services = Services(out var engine);
        engine.Returns("Entities.FindByName", answer);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(InfoLightingRelative).Assembly);
        registry.Services = services;

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, EntityNames.InfoLightingRelative, key, value);

        var warnings = new List<string>();
        registry.Lines.Tap = (message, isWarning) => { if (isWarning) warnings.Add(message); };
        try
        {
            registry.Spawn(Ent, EntityNames.InfoLightingRelative);
        }
        finally
        {
            registry.Lines.Tap = null;
        }

        return (registry, (InfoLightingRelative)registry.Get(Ent)!, warnings);
    }

    /// <summary>Runs the deferred think with the log captured.</summary>
    private static void ThinkCapturing(ScriptedEntityRegistry registry, List<string> warnings)
    {
        registry.Lines.Tap = (message, isWarning) => { if (isWarning) warnings.Add(message); };
        try
        {
            registry.Think(Ent);
        }
        finally
        {
            registry.Lines.Tap = null;
        }
    }

    /// <summary>
    /// The classname spawns, and the landmark is NOT resolved until the think.
    /// The second half is the one worth having: at spawn the map is still being
    /// read, so a lookup there would find nothing and report it as a missing
    /// landmark.
    /// </summary>
    [Fact]
    public void TheLandmarkIsResolvedAfterTheMapHasLoadedAndNotAtSpawn()
    {
        var (registry, ent, warnings) = SpawnWith(Landmark,
            ("targetname", "sink_lighting"), ("LightingLandmark", "somewhere_else"));

        Assert.True(ent.Vars.LightingLandmark == "somewhere_else",
            "the LightingLandmark key is read off the map, with stock's spelling");
        Assert.True(!ent.HasLandmark,
            "at spawn the landmark is still unresolved — the map is not loaded yet");
        Assert.True(warnings.Count == 0,
            "and an unresolved landmark at spawn is not yet a mapper's mistake");

        ThinkCapturing(registry, warnings);

        Assert.True(ent.Landmark == Landmark && ent.HasLandmark,
            "the first think resolves the landmark by name");
        Assert.True(warnings.Count == 0,
            "a landmark that was found warns about nothing, but got: "
                + string.Join(" | ", warnings));
    }

    /// <summary>
    /// A landmark name that finds nothing warns and names both entities, which
    /// is the only way a mapper learns they mistyped it. Stock's DevWarning did
    /// exactly this and it is the one observable behaviour of the class.
    /// </summary>
    [Fact]
    public void AMissingLandmarkTellsTheMapperWhichNameFailed()
    {
        // EntityHandle.None, and NOT default(EntityHandle) — the engine's miss
        // value is INVALID_EHANDLE_INDEX, whereas a defaulted handle holds 0,
        // which is worldspawn. A test written with `default` here would be
        // handing the entity THE WORLD and calling it "not found".
        var (registry, ent, warnings) = SpawnWith(EntityHandle.None,
            ("LightingLandmark", "typo_here"));

        ThinkCapturing(registry, warnings);

        Assert.True(!ent.HasLandmark, "nothing was found, so there is no landmark");
        Assert.True(warnings.Count == 1, "and exactly one warning is produced");
        Assert.True(warnings[0].Contains("typo_here", StringComparison.Ordinal),
            "the warning names the landmark that could not be found");
        Assert.True(warnings[0].Contains(EntityNames.InfoLightingRelative, StringComparison.Ordinal),
            "and names the entity that asked for it");
    }

    /// <summary>
    /// No LightingLandmark key at all is not an error — it is
    /// <c>wall2_lighting_rel</c>, one of the two instances dm_lockdown actually
    /// places. Stock resolves it to null and says nothing, and a port that
    /// warned here would print on every load of the shipping map.
    /// </summary>
    [Fact]
    public void NoLandmarkKeyIsSilentAndLooksUpNothing()
    {
        // The double is STRICT and "Entities.FindByName" is deliberately not
        // stubbed: if the entity looks a name up despite having none, this test
        // fails naming the call rather than passing on a default.
        var services = Services(out var engine);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(InfoLightingRelative).Assembly);
        registry.Services = services;

        registry.KeyValue(Ent, EntityNames.InfoLightingRelative, "targetname", "wall2_lighting_rel");
        registry.Spawn(Ent, EntityNames.InfoLightingRelative);

        var ent = (InfoLightingRelative)registry.Get(Ent)!;

        var warnings = new List<string>();
        ThinkCapturing(registry, warnings);

        Assert.True(!ent.HasLandmark, "no landmark was named, so there is none");
        Assert.True(warnings.Count == 0, "and a landmark nobody named is not a mistake");
    }
}
