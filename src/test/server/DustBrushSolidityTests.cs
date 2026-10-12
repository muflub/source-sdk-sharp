using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dust volumes' brushes must be SOLID to the engine's point test, because
/// that is the only thing that puts a mote anywhere.
///
/// <para>
/// THE DEFECT THESE PIN. <c>8e-render</c> measured
/// <c>spawn: 4050 call(s), 0 found solid, 0 particle(s) made</c> on
/// <c>ss_sandbox</c> — 40,500 point tests inside the dust brush's own world
/// bounds, and <c>enginetrace->GetPointContents_Collideable</c> never once
/// answered <c>CONTENTS_SOLID</c>. Every layer above the map was blamed in
/// turn: the model index, the model type, the collideable's solid flags, the
/// transform, the engine's static-prop handling in the point test.
/// </para>
///
/// <para>
/// The engine was right every time. <c>Sandbox.cs</c> textured the dust volumes
/// with <c>TOOLS/TOOLSINVISIBLE</c>, and vbsp's <c>%compileInvisible</c> does
/// <c>contents &amp;= ~CONTENTS_SOLID; contents |= CONTENTS_GRATE</c>
/// (<c>src/utils/vbsp/textures.cpp:202-209</c>), after which
/// <c>BrushContents</c> adds <c>CONTENTS_TRANSLUCENT</c> and clears
/// <c>CONTENTS_SOLID</c> again (<c>src/utils/vbsp/map.cpp:431-436</c>). The
/// shipped brush read <c>0x10000008</c>. Stock's own
/// <c>C_Func_Dust::AttemptSpawnNewParticle</c> tests
/// <c>contents &amp; CONTENTS_SOLID</c>, so stock, unported, would have emitted
/// exactly as many motes on this map: none.
/// </para>
///
/// <para>
/// WHAT A CORRECT DUST BRUSH LOOKS LIKE, measured rather than reasoned about.
/// Valve's own map sources texture <c>func_dustmotes</c> with
/// <c>TOOLS/TOOLSTRIGGER</c> (<c>cs_militia.vmf</c> x7,
/// <c>sdk_ctf_2fort.vmf</c>), which sets surface FLAGS only and leaves the
/// contents word alone — so <c>map.cpp:2745</c>'s "no visible contents" rule
/// gives it <c>CONTENTS_SOLID</c>. Read back out of six shipped TF2 maps
/// (cp_foundry *116, cp_gorge *59, cp_granary *94, cp_gravelpit *73,
/// cp_snakewater_final1 *54, ctf_turbine *61), every <c>func_dustmotes</c>
/// brush model carries contents <c>0x00000001</c> exactly.
/// </para>
///
/// <para>
/// TWO LAYERS, DELIBERATELY. The generator fact goes red the moment somebody
/// edits <c>Sandbox.cs</c> back, in every tree, with no map on disk; the
/// artefact fact goes red if the map that actually SHIPS says otherwise,
/// whatever the generator claims. A regression that reaches a player has to get
/// past both, and the pair is the reason the original defect could hide: every
/// check in the tree scored the generator, and the generator never mentioned
/// contents at all.
/// </para>
/// </summary>
public class DustBrushSolidityTests
{
    /// <summary>The two classnames that spawn motes by point-testing their own brush.</summary>
    private static readonly string[] DustClasses = ["func_dustmotes", "func_dustcloud"];

    /// <summary>
    /// What Valve textures a dust brush with, and the only value that keeps the
    /// contents word at <c>CONTENTS_SOLID</c> while drawing nothing.
    /// </summary>
    private const string DustBrushMaterial = "TOOLS/TOOLSTRIGGER";

    /// <summary>
    /// Tool materials whose vbsp keys CLEAR <c>CONTENTS_SOLID</c>, from
    /// <c>src/utils/vbsp/textures.cpp</c>. A dust brush carrying any of these
    /// emits nothing, and each one reads to a mapper like "invisible volume",
    /// which is how the wrong one got picked.
    /// </summary>
    private static readonly string[] MaterialsThatClearSolid =
    [
        "TOOLS/TOOLSINVISIBLE",   // %compileInvisible -> CONTENTS_GRATE
        "TOOLS/TOOLSNODRAWWATER", // %compileWater
        "TOOLS/TOOLSCLIP",        // %compileClip
        "TOOLS/TOOLSPLAYERCLIP",  // %compileClip
        "TOOLS/TOOLSNPCCLIP",     // %compileNpcClip
        "TOOLS/TOOLSSKIP",        // %compileSkip -> no contents at all
        "TOOLS/TOOLSHINT",        // %compileHint -> no contents at all
    ];

    private static string RequireThisTreesMap()
    {
        SandboxMapLookup lookup = SandboxArtefact.Locate(RepoLayout.SdkTree);

        Assert.True(lookup.State == SandboxMapState.Present, lookup.Explanation);

        return lookup.Path!;
    }

    private static List<VmfEntity> DustVolumesInTheGenerator()
    {
        VmfMap map = Sandbox.Build();

        return map.Entities
            .Where(e => DustClasses.Contains(e.ClassName, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    // ---------------------------------------------------------------------
    // The generator. No map on disk needed, so these run in every tree.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The map has dust to score at all. Without this the two facts below are
    /// vacuously green on a generator that stopped placing dust entirely — which
    /// is the shape of "fix" a compile gate selects for.
    /// </summary>
    [Fact]
    public void TheGeneratorStillPlacesBothDustVolumes()
    {
        List<VmfEntity> dust = DustVolumesInTheGenerator();

        Assert.Equal(
            new[] { "func_dustcloud", "func_dustmotes" },
            dust.Select(e => e.ClassName.ToLowerInvariant()).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every face of every dust brush carries the material Valve's maps use.
    /// Equality on the whole SET rather than a "contains" needle: a brush that
    /// gained a second, contents-clearing face would pass a needle and emit
    /// nothing.
    /// </summary>
    [Fact]
    public void TheGeneratorTexturesTheDustVolumesTheWayValvesMapsDo()
    {
        foreach (VmfEntity dust in DustVolumesInTheGenerator())
        {
            string[] materials = dust.Solids
                .SelectMany(s => s.Sides)
                .Select(s => s.Material)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(new[] { DustBrushMaterial }, materials);
        }
    }

    /// <summary>
    /// Stated as a refusal as well as an equality, so the log says WHY when the
    /// value moves rather than only that it moved.
    /// </summary>
    [Fact]
    public void NoDustBrushFaceCarriesAToolMaterialThatClearsContentsSolid()
    {
        foreach (VmfEntity dust in DustVolumesInTheGenerator())
        {
            foreach (VmfSide side in dust.Solids.SelectMany(s => s.Sides))
            {
                Assert.False(
                    MaterialsThatClearSolid.Contains(side.Material, StringComparer.OrdinalIgnoreCase),
                    $"{dust.ClassName} '{dust.Name}' has a face textured {side.Material}, which vbsp "
                    + "compiles with CONTENTS_SOLID CLEARED. AttemptSpawnNewParticle only makes a mote "
                    + "where GetPointContents_Collideable answers CONTENTS_SOLID, so this volume will "
                    + "draw nothing at all. Use " + DustBrushMaterial + ".");
            }
        }
    }

    // ---------------------------------------------------------------------
    // The artefact. What the player's machine actually loads.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The defect itself, read out of the compiled map: every brush of every
    /// dust volume's submodel must carry <c>CONTENTS_SOLID</c>.
    /// </summary>
    [SandboxMapFact]
    public void TheShippedMapsDustVolumesAreSolidToThePointTest()
    {
        string bsp = RequireThisTreesMap();

        foreach (string className in DustClasses)
        {
            var volumes = BspBrushContents.BrushesOfClass(bsp, className);

            Assert.True(volumes.Count > 0,
                $"MAP DISAGREES WITH THE GENERATOR — {bsp} contains no {className}, though "
                + "Sandbox.cs places one in the weather station. This map is STALE, not missing; "
                + "recompile it with `make map MAP=ss_sandbox`.");

            foreach ((string name, IReadOnlyList<BspBrushContents.Brush> brushes) in volumes)
            {
                Assert.True(brushes.Count > 0,
                    $"{className} '{name}' in {bsp} has no brushes at all, so there is nowhere "
                    + "for a mote to go.");

                foreach (BspBrushContents.Brush brush in brushes)
                {
                    Assert.True(
                        (brush.Contents & BspBrushContents.ContentsSolid) != 0,
                        $"DUST DRAWS NOTHING — {className} '{name}' in {bsp} has brush "
                        + $"{brush.Index} with contents 0x{brush.Contents:x8}, and CONTENTS_SOLID "
                        + "(0x1) is not set"
                        + (((brush.Contents & BspBrushContents.ContentsGrate) != 0)
                            ? " (it is CONTENTS_GRATE, which is what TOOLS/TOOLSINVISIBLE compiles to)"
                            : string.Empty)
                        + ". AttemptSpawnNewParticle makes a mote only where "
                        + "GetPointContents_Collideable answers CONTENTS_SOLID, so this volume emits "
                        + "nothing. Fix the material in Sandbox.cs and recompile with "
                        + "`make map MAP=ss_sandbox`.");
                }
            }
        }
    }

    /// <summary>
    /// The reader is not answering "solid" about everything. The world brush
    /// model (*0) holds the map's own geometry AND its non-solid volumes, so a
    /// reader stuck at <c>CONTENTS_SOLID</c> cannot produce this.
    /// </summary>
    [SandboxMapFact]
    public void TheReaderReportsContentsOtherThanSolidWhereTheMapHasThem()
    {
        string bsp = RequireThisTreesMap();

        IReadOnlyList<BspBrushContents.Brush> world = BspBrushContents.BrushesOfModel(bsp, 0);

        Assert.True(world.Count > 0, $"{bsp}'s worldspawn submodel has no brushes");

        Assert.Contains(world, b => (b.Contents & BspBrushContents.ContentsSolid) != 0);
        Assert.Contains(world, b => (b.Contents & BspBrushContents.ContentsSolid) == 0);
    }

    // ---------------------------------------------------------------------
    // The reader's own refusals. No map needed.
    // ---------------------------------------------------------------------

    [Fact]
    public void AFileThatIsNotABspIsRefusedRatherThanReadAsEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, new byte[8 + (64 * 16)]);

        try
        {
            var thrown = Assert.Throws<InvalidDataException>(
                () => BspBrushContents.BrushesOfModel(path, 0));

            Assert.Contains("does not start with VBSP", thrown.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SandboxMapFact]
    public void ASubmodelTheMapDoesNotHaveIsRefusedRatherThanReadAsEmpty()
    {
        string bsp = RequireThisTreesMap();

        var thrown = Assert.Throws<InvalidDataException>(
            () => BspBrushContents.BrushesOfModel(bsp, 1_000_000));

        Assert.Contains("is not one of them", thrown.Message);
    }
}
