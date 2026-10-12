using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The compiled map, rather than the generator that is supposed to have made
/// it.
///
/// <para>
/// EVERY OTHER SANDBOX CHECK SCORES THE GENERATOR. SandboxTests.Coverage reads
/// the [ScriptedEntity] attributes and looks for them in Sandbox.Build(), which
/// is the thing that placed them. SandboxPassTests.TheSandboxsOwnExplosionSurvivesItsResetButton
/// reads a spawnflag out of Sandbox.Build() and checks the value Sandbox.cs
/// just wrote. Both can report a gap only if the generator disagrees with
/// itself, and neither has ever looked at ss_sandbox.bsp — the file that is
/// actually loaded when anybody types "map ss_sandbox".
/// </para>
///
/// <para>
/// So the map drifted. It sat 72 classnames behind the generator once, and in
/// the checkouts that carry a copy of it, it still does: measured with
/// `MapGen mapcheck`, that ss_sandbox.bsp holds 733 entities across 194
/// classnames where the generator produces 944, is short 72 of the classes the
/// mod registers, and carries spawnflags 0 on the effects station's
/// env_explosion where Sandbox.cs says 2 — so on that map, the reset button
/// deletes the entity the station exists to demonstrate. That is the exact
/// failure the generator-side test was written to prevent, and it had been live
/// in the artefact the whole time the test was green.
/// </para>
///
/// <para>
/// THE REASONING THAT LEFT THE GAP, because it reads as sound and is not: a
/// .bsp needs Wine, a Steam depot and the HL2 content to COMPILE, `make test`
/// deliberately has none of those, therefore `make test` cannot check a .bsp.
/// The conclusion does not follow from the premise. Producing a map needs the
/// toolchain; reading one needs BspEntities and a file that is already sitting
/// in game/mod_sharp/maps. The coverage rule was put behind `make map` for a
/// constraint that only ever applied to building.
/// </para>
///
/// <para>
/// WHICH MAP IS "THE" MAP — the second gap, and the one that taxed four other
/// lanes. This used to locate the .bsp by walking up from the test binary until
/// a game/mod_sharp/maps/ss_sandbox.bsp turned up anywhere above it. Agent
/// worktrees are nested inside the main checkout, so when a worktree had no map
/// of its own the walk left the worktree and scored the MAIN CHECKOUT's map
/// instead: a file the tree under test does not contain, did not build and
/// cannot fix. <see cref="SandboxArtefact"/> now finds the nearest enclosing
/// checkout root first and looks in exactly one place inside it, so a tree
/// without a map reports that it has none rather than borrowing a neighbour's.
/// </para>
///
/// <para>
/// The one gap left, stated rather than hidden: a tree that has never compiled
/// the map has no .bsp to read, since it is build output and gitignored, and
/// this SKIPS there — visibly, via <see cref="SandboxMapFactAttribute"/>, which
/// prints the reason rather than passing silently. That skip is why
/// <see cref="SandboxStalenessTests"/> exercises the same judgement on literal
/// inputs — the logic is covered everywhere, and only the reading of one real
/// file is not.
/// </para>
/// </summary>
public class SandboxArtefactTests
{
    /// <summary>
    /// This tree's compiled map.
    ///
    /// <para>
    /// A skipped test never reaches this, so the only state left to reject is
    /// <see cref="SandboxMapState.NoTreeFound"/> — the layout moved and the
    /// binary cannot tell which checkout it belongs to. That fails, loudly,
    /// because it means these tests no longer know what they are scoring.
    /// </para>
    /// </summary>
    private static string RequireThisTreesMap()
    {
        SandboxMapLookup lookup = SandboxArtefact.Locate(RepoLayout.SdkTree);

        Assert.True(lookup.State == SandboxMapState.Present, lookup.Explanation);

        return lookup.Path!;
    }

    [SandboxMapFact]
    public void TheShippedMapAgreesWithTheGeneratorThatProducedIt()
    {
        string bsp = RequireThisTreesMap();

        List<SandboxPass.DeclaredEntity> generated = SandboxPass.FromGenerator(Sandbox.Build());
        List<SandboxPass.DeclaredEntity> compiled = BspEntities.Read(bsp);

        var drifts = SandboxPass.Staleness(generated, compiled);

        // The first few and a count, not all of them: one station added to
        // Sandbox.cs drifts every entity in it at once, and two hundred lines
        // bury the one that explains the rest.
        string detail = string.Join("\n  ", drifts.Take(8).Select(d => d.ToString()));

        // "MAP DISAGREES WITH THE GENERATOR" against the skip's "NO COMPILED MAP
        // IN THIS TREE": the two states this can report are meant to be told
        // apart from the log alone, so they are labelled rather than merely
        // worded differently.
        Assert.True(drifts.Count == 0,
            $"MAP DISAGREES WITH THE GENERATOR — {bsp} is behind Sandbox.cs in "
            + $"{drifts.Count} place(s). It holds {compiled.Count} entities where Sandbox.cs "
            + $"produces {generated.Count}. This map is STALE, not missing; recompile it with "
            + "`make map MAP=ss_sandbox`, which needs the Windows map tools:\n  " + detail);
    }

    /// <summary>
    /// The named defect on its own, so that the one keyvalue everybody has been
    /// told about is called out by name rather than being one line inside a
    /// count of two hundred.
    /// </summary>
    [SandboxMapFact]
    public void TheShippedMapsExplosionSurvivesItsResetButton()
    {
        string bsp = RequireThisTreesMap();

        var explosions = BspEntities.Read(bsp)
            .Where(e => string.Equals(e.ClassName, "env_explosion", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(explosions.Count > 0,
            $"MAP DISAGREES WITH THE GENERATOR — {bsp} contains no env_explosion at all, "
            + "though Sandbox.cs places one in the effects station. This map is STALE, not "
            + "missing; recompile it with `make map MAP=ss_sandbox`.");

        foreach (var explosion in explosions)
        {
            explosion.KeyValues.TryGetValue("spawnflags", out string? flags);

            Assert.True((int.Parse(flags ?? "0") & 2) != 0,
                $"MAP DISAGREES WITH THE GENERATOR — the env_explosion \"{explosion.TargetName}\" "
                + $"in the COMPILED map {bsp} carries SF_ENVEXPLOSION_REPEATABLE (2), or the "
                + "first press of the station's reset button ends in UTIL_Remove and every "
                + "press after it reaches nothing. This map is STALE, not missing; recompile "
                + "it with `make map MAP=ss_sandbox`.");
        }
    }
}
