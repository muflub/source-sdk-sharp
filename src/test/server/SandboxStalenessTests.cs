using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="SandboxPass.Staleness"/> on literal inputs.
///
/// <para>
/// <see cref="SandboxArtefactTests"/> points the same judgement at the real
/// ss_sandbox.bsp, and skips when a tree has never compiled one. These do not
/// skip and do not read a file, so the logic that decides what "stale" means is
/// covered in every tree — including the ones where the artefact test has
/// nothing to look at.
/// </para>
///
/// <para>
/// Both directions matter. A comparison that reports nothing is a guard that
/// cannot fail, which is the state this whole area was in; a comparison that
/// reports everything gets muted within a week, which is how it would end up
/// back there.
/// </para>
/// </summary>
public class SandboxStalenessTests
{
    private static SandboxPass.DeclaredEntity Ent(
        string className, string targetName, params string[] keyValues)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i + 1 < keyValues.Length; i += 2)
            pairs[keyValues[i]] = keyValues[i + 1];

        return new SandboxPass.DeclaredEntity(className, targetName, pairs);
    }

    // ---------------------------------------------------------------------
    // What must be reported.
    // ---------------------------------------------------------------------

    [Fact]
    public void AKeyvalueTheCompiledMapDisagreesOnIsDrift()
    {
        // The live defect, in miniature: the map has every entity, and one of
        // them carries a value from before the generator was fixed.
        var generated = new[] { Ent("env_explosion", "emitters_bang", "spawnflags", "2") };
        var compiled = new[] { Ent("env_explosion", "emitters_bang", "spawnflags", "0") };

        var drifts = SandboxPass.Staleness(generated, compiled);

        var drift = Assert.Single(drifts);
        Assert.Equal("spawnflags", drift.Key);
        Assert.Equal("2", drift.Generated);
        Assert.Equal("0", drift.Compiled);
    }

    [Fact]
    public void AnEntityMissingFromTheCompiledMapIsDrift()
    {
        // A whole station added to Sandbox.cs after the last compile. This is
        // the bulk of the real drift: 207 named entities of it.
        var generated = new[]
        {
            Ent("prop_dynamic", "sprites_post"),
            Ent("env_sprite", "sprites_plain", "model", "sprites/glow01.vmt"),
        };

        var compiled = new[] { Ent("prop_dynamic", "sprites_post") };

        var drift = Assert.Single(SandboxPass.Staleness(generated, compiled));

        Assert.Equal("env_sprite", drift.ClassName);
        Assert.Equal("sprites_plain", drift.TargetName);
        Assert.Null(drift.Key);
    }

    [Fact]
    public void AKeyTheCompiledMapDroppedEntirelyIsDrift()
    {
        // Not the same as a changed value, and it must not read as agreement
        // just because there is nothing on the other side to compare.
        var generated = new[] { Ent("func_button", "reset", "wait", "1") };
        var compiled = new[] { Ent("func_button", "reset") };

        var drift = Assert.Single(SandboxPass.Staleness(generated, compiled));

        Assert.Equal("wait", drift.Key);
        Assert.Equal("1", drift.Generated);
        Assert.Null(drift.Compiled);
    }

    [Fact]
    public void AWorldCoordinateKeyThatIsNotOriginIsStillCompared()
    {
        // Eight of the nine real keyvalue drifts are these: lowerleft,
        // attachpoint, springaxis, position2, slideaxis and friends. They are
        // absolute points that move when the station layout does, vbsp passes
        // them through unchanged, and exempting them for looking like origin
        // would drop most of the evidence that the map is behind.
        var generated = new[] { Ent("phys_lengthconstraint", "rope", "attachpoint", "-960 224 48") };
        var compiled = new[] { Ent("phys_lengthconstraint", "rope", "attachpoint", "-1600 544 48") };

        Assert.Single(SandboxPass.Staleness(generated, compiled));
    }

    // ---------------------------------------------------------------------
    // What must NOT be reported. Each of these would fire on every run of a
    // perfectly fresh map, and a check that fires on a fresh map gets muted.
    // ---------------------------------------------------------------------

    [Fact]
    public void AFreshMapReportsNothing()
    {
        var entities = new[]
        {
            Ent("env_explosion", "emitters_bang", "spawnflags", "2", "iMagnitude", "60"),
            Ent("func_button", "emitters_button", "wait", "1"),
        };

        Assert.Empty(SandboxPass.Staleness(entities, entities));
    }

    [Fact]
    public void TheCompilerRewritingOriginAndAnglesIsNotDrift()
    {
        var generated = new[] { Ent("env_sprite", "glow", "origin", "0 160 24", "angles", "0 0 0") };
        var compiled = new[] { Ent("env_sprite", "glow", "origin", "1600 800 24", "angles", "-0 0 0") };

        Assert.Empty(SandboxPass.Staleness(generated, compiled));
    }

    [Fact]
    public void TheVmfWritersHammeridIsNotDrift()
    {
        // Renumbers whenever anything is inserted above it, so every entity in
        // the map would drift the first time a station was added.
        var generated = new[] { Ent("env_sprite", "glow", "hammerid", "11") };
        var compiled = new[] { Ent("env_sprite", "glow", "hammerid", "2417") };

        Assert.Empty(SandboxPass.Staleness(generated, compiled));
    }

    [Fact]
    public void AnEntityOnlyTheCompiledMapHasIsNotDrift()
    {
        // vbsp synthesises entities of its own, and worldspawn is never a
        // generator entity. The comparison is one-directional on purpose.
        var generated = new[] { Ent("env_sprite", "glow") };

        var compiled = new[]
        {
            Ent("env_sprite", "glow"),
            Ent("worldspawn", ""),
            Ent("env_cubemap", "auto"),
        };

        Assert.Empty(SandboxPass.Staleness(generated, compiled));
    }

    [Fact]
    public void AnEntityTheCompilerDeletesIsNotDrift()
    {
        // light and info_null never reach the lump, in this port or in stock
        // Source.
        var generated = new[] { Ent("light", "lamp", "_light", "255 255 255 200") };

        Assert.Empty(SandboxPass.Staleness(generated, Array.Empty<SandboxPass.DeclaredEntity>()));
    }

    [Fact]
    public void AnUnnamedEntityIsNotMatchedByPosition()
    {
        // Matching an unnamed entity would mean guessing which of several
        // identical ones it is, and reporting drift whenever one moved.
        var generated = new[] { Ent("prop_physics", "", "model", "props/box.mdl") };

        Assert.Empty(SandboxPass.Staleness(generated, Array.Empty<SandboxPass.DeclaredEntity>()));
    }

    [Fact]
    public void OneOfSeveralEntitiesSharingANameAgreeingIsEnough()
    {
        // Which of the identical copies a reset button hits is not something
        // this check has an opinion about.
        var generated = new[] { Ent("env_sprite", "marks", "renderamt", "200") };

        var compiled = new[]
        {
            Ent("env_sprite", "marks", "renderamt", "64"),
            Ent("env_sprite", "marks", "renderamt", "200"),
        };

        Assert.Empty(SandboxPass.Staleness(generated, compiled));
    }

    // ---------------------------------------------------------------------
    // The check on the check.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheExemptKeyListIsShort()
    {
        // Every key exempted from the comparison is a key a stale map can drift
        // in unseen, so the list is the cost of the check. Three is what the
        // compiler and the .vmf writer actually own; a fourth needs a reason
        // written down beside it, and this is where that gets noticed.
        string[] exempt = ["origin", "angles", "hammerid"];

        Assert.All(exempt, key => Assert.True(SandboxPass.KeyIsCompilerOwned(key)));

        foreach (string key in new[] { "spawnflags", "model", "targetname", "renderamt",
                                       "attachpoint", "lowerleft", "iMagnitude" })
        {
            Assert.False(SandboxPass.KeyIsCompilerOwned(key),
                $"{key} is compared, not exempted — a stale map drifts in it");
        }
    }

    [Fact]
    public void TheGeneratorsMapConvertsWithItsKeyvaluesIntact()
    {
        // FromGenerator is the adapter between the two shapes, and an adapter
        // that quietly dropped keyvalues would make Staleness report nothing
        // for the same reason an empty comparison does.
        var declared = SandboxPass.FromGenerator(Sandbox.Build());

        Assert.NotEmpty(declared);

        var explosion = declared.FirstOrDefault(e => e.ClassName == "env_explosion");

        Assert.NotNull(explosion);
        Assert.True(explosion.KeyValues.TryGetValue("spawnflags", out string? flags),
            "the converted generator entity kept its spawnflags");
        Assert.Equal("2", flags);
    }
}
