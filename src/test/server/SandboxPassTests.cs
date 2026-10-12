using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The arithmetic behind Part 6's Phase 4 gate.
///
/// <c>tools/sandbox-pass</c> collects two files from a running game and this
/// decides what they mean. The decision is the part worth testing: collecting is
/// a shell loop that fails loudly, and judging is where a gate quietly stops
/// being able to fail. Every excuse the audit accepts has a test that it does
/// NOT stretch one entity further than the map earns.
/// </summary>
public class SandboxPassTests
{
    private static SandboxPass.DeclaredEntity Ent(
        string className, string targetName = "", params string[] keyValues)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i + 1 < keyValues.Length; i += 2)
            pairs[keyValues[i]] = keyValues[i + 1];

        return new SandboxPass.DeclaredEntity(className, targetName, pairs);
    }

    [Fact]
    public void AnEntityTheServerDoesNotHaveFailsThePass()
    {
        var declared = new[] { Ent("prop_dynamic", "sign") };
        var live = new Dictionary<string, int> { ["prop_dynamic"] = 0 };

        var shortfalls = SandboxPass.Audit(declared, live);

        Assert.Single(shortfalls);
        Assert.True(shortfalls[0].Fails, "a declared entity with nothing alive fails the pass");
        Assert.Null(shortfalls[0].Excuse);
    }

    [Fact]
    public void AClassnameMissingFromTheLiveAnswersCountsAsZero()
    {
        // A query that was never asked and a query that answered nothing are the
        // same failure. Treating an absent key as "not measured, so fine" is how
        // a broken collector reads as a green gate.
        var shortfalls = SandboxPass.Audit(
            new[] { Ent("func_door", "door") }, new Dictionary<string, int>());

        Assert.Single(shortfalls);
        Assert.True(shortfalls[0].Fails, "a classname nobody answered for is short, not excused");
    }

    [Fact]
    public void EveryEntityAliveIsAPassWithNothingToReport()
    {
        var declared = new[] { Ent("prop_dynamic", "a"), Ent("prop_dynamic", "b"), Ent("func_door", "d") };
        var live = new Dictionary<string, int> { ["prop_dynamic"] = 2, ["func_door"] = 1 };

        Assert.Empty(SandboxPass.Audit(declared, live));
    }

    [Fact]
    public void MoreAliveThanDeclaredIsNotAFailure()
    {
        // Entities spawn helpers, and a station's reset can leave extra copies
        // behind — env_entity_maker does exactly that. The gate is about what is
        // MISSING; counting surplus as a fault would make it fire on a healthy
        // map and get switched off.
        var live = new Dictionary<string, int> { ["prop_physics"] = 9 };

        Assert.Empty(SandboxPass.Audit(new[] { Ent("prop_physics", "crate") }, live));
    }

    [Fact]
    public void TheCompilersOwnRemovalsAreExcused()
    {
        var declared = new[] { Ent("light"), Ent("light"), Ent("info_null") };
        var live = new Dictionary<string, int> { ["light"] = 0, ["info_null"] = 0 };

        var shortfalls = SandboxPass.Audit(declared, live);

        Assert.Equal(2, shortfalls.Count);
        Assert.All(shortfalls, s => Assert.False(s.Fails, "a baked light and a deleted info_null are excused"));
    }

    [Fact]
    public void WorldspawnIsNotScoredAtAll()
    {
        // Entity 0 is not enumerable by classname, so a live count for it is
        // always zero and always meaningless.
        Assert.Empty(SandboxPass.Audit(new[] { Ent("worldspawn") }, new Dictionary<string, int>()));
    }

    [Fact]
    public void ATemplateIsExcusedBecauseThePointTemplateNamesIt()
    {
        var declared = new[]
        {
            Ent("point_template", "maker_tmpl", "Template01", "stock"),
            Ent("prop_physics", "stock"),
        };

        var live = new Dictionary<string, int> { ["point_template"] = 1, ["prop_physics"] = 0 };

        var shortfalls = SandboxPass.Audit(declared, live);

        Assert.Single(shortfalls);
        Assert.False(shortfalls[0].Fails, "an entity a point_template claims is taken by design");
        Assert.Contains("point_template", shortfalls[0].Excuse!);
    }

    [Fact]
    public void TheTemplateExcuseDoesNotStretchToASecondEntity()
    {
        // The failure mode this guards: one legitimate absence teaching the gate
        // to forgive the whole classname. Two crates declared, one of them a
        // template, and BOTH missing — that is still a defect.
        var declared = new[]
        {
            Ent("point_template", "maker_tmpl", "Template01", "stock"),
            Ent("prop_physics", "stock"),
            Ent("prop_physics", "crate"),
        };

        var live = new Dictionary<string, int> { ["point_template"] = 1, ["prop_physics"] = 0 };

        var shortfalls = SandboxPass.Audit(declared, live);

        Assert.Single(shortfalls);
        Assert.True(shortfalls[0].Fails, "one template does not excuse a second missing entity");
        Assert.Equal(1, shortfalls[0].Excused);
    }

    [Fact]
    public void ATemplateNameIsOnlyExcusedWhenAPointTemplateClaimsIt()
    {
        // The same map without the point_template: nothing claims "stock", so
        // its absence is a defect. This is what keeps the excuse derived rather
        // than a name that happens to look template-ish.
        var shortfalls = SandboxPass.Audit(
            new[] { Ent("prop_physics", "stock") },
            new Dictionary<string, int> { ["prop_physics"] = 0 });

        Assert.Single(shortfalls);
        Assert.True(shortfalls[0].Fails, "a template name nobody declared as one is not excused");
    }

    [Fact]
    public void EveryTemplateSlotIsRead()
    {
        var declared = new[]
        {
            Ent("point_template", "t", "Template01", "one", "Template02", "two", "Template03", ""),
        };

        var names = SandboxPass.TemplateTargets(declared);

        Assert.Equal(2, names.Count);
        Assert.Contains("one", names);
        Assert.Contains("two", names);
    }

    [Fact]
    public void TheMapEntityLumpIsParsedAsDebugApiAnswersIt()
    {
        const string json = """
        {"map":"ss_sandbox","entities":[
          {"id":0,"classname":"worldspawn","targetname":"","keyvalues":[{"key":"skyname","value":"sky_day01_01"}]},
          {"id":7,"classname":"prop_physics","targetname":"stock","keyvalues":[{"key":"model","value":"crate.mdl"}]}
        ]}
        """;

        var declared = SandboxPass.ReadMapEntities(json);

        Assert.Equal(2, declared.Count);
        Assert.Equal("prop_physics", declared[1].ClassName);
        Assert.Equal("stock", declared[1].TargetName);
        Assert.Equal("crate.mdl", declared[1].KeyValues["model"]);
    }

    [Fact]
    public void TheLiveCountsAreParsedOneAnswerPerLine()
    {
        string[] lines =
        [
            """{"class":"prop_physics","entities":[],"count":47}""",
            "",
            """{"class":"func_door","entities":[],"count":1}""",
        ];

        var counts = SandboxPass.ReadLiveCounts(lines);

        Assert.Equal(2, counts.Count);
        Assert.Equal(47, counts["prop_physics"]);
        Assert.Equal(1, counts["func_door"]);
    }

    [Fact]
    public void TheReportEndsWithAVerdictEitherWay()
    {
        var declared = new[] { Ent("prop_dynamic", "sign") };

        var none = Array.Empty<SandboxPass.Absence>();

        var passing = SandboxPass.Report(declared,
            new Dictionary<string, int> { ["prop_dynamic"] = 1 },
            SandboxPass.Audit(declared, new Dictionary<string, int> { ["prop_dynamic"] = 1 }),
            none).ToList();

        var failing = SandboxPass.Report(declared,
            new Dictionary<string, int> { ["prop_dynamic"] = 0 },
            SandboxPass.Audit(declared, new Dictionary<string, int> { ["prop_dynamic"] = 0 }),
            none).ToList();

        Assert.StartsWith("PASS", passing[^1]);
        Assert.StartsWith("FAIL", failing[^1]);

        // A map that is fully alive but does not CONTAIN a registered class
        // fails too, and the verdict has to say so — the whole point of the new
        // leg is that "everything declared is alive" was never the whole
        // question.
        var short_ = SandboxPass.Report(declared,
            new Dictionary<string, int> { ["prop_dynamic"] = 1 },
            SandboxPass.Audit(declared, new Dictionary<string, int> { ["prop_dynamic"] = 1 }),
            new[] { new SandboxPass.Absence("env_smokestack", null) }).ToList();

        Assert.StartsWith("FAIL", short_[^1]);
    }

    /// <summary>
    /// The map's own side of the defect this gate found: the effects station's
    /// env_explosion must carry SF_ENVEXPLOSION_REPEATABLE (2), or the first
    /// press of its reset button deletes the entity the station demonstrates.
    ///
    /// This is the GENERATOR's side, and on its own it guards the wrong thing.
    /// It reads a spawnflag out of Sandbox.Build() and checks the value
    /// Sandbox.cs just wrote, so it can report a gap only if the generator
    /// disagrees with itself — and it stayed green for the whole time
    /// ss_sandbox.bsp carried spawnflags 0 on that very entity. The map anyone
    /// actually loads is checked by
    /// SandboxArtefactTests.TheShippedMapsExplosionSurvivesItsResetButton.
    ///
    /// Kept, rather than replaced by it: this one fails the moment somebody
    /// edits the value in Sandbox.cs, before a map is ever compiled, and it
    /// cannot skip.
    /// </summary>
    [Fact]
    public void TheSandboxsOwnExplosionSurvivesItsResetButton()
    {
        var map = Sandbox.Build();

        var explosions = map.Entities
            .Where(e => e.ClassName == "env_explosion")
            .ToList();

        Assert.NotEmpty(explosions);

        foreach (var explosion in explosions)
        {
            string flags = explosion.KeyValues
                .Where(kv => kv.Key == "spawnflags")
                .Select(kv => kv.Value)
                .FirstOrDefault() ?? "0";

            Assert.True((int.Parse(flags) & 2) != 0,
                "an env_explosion a reset button fires is repeatable, or the reset destroys it");
        }
    }

    //-------------------------------------------------------------------------
    // The coverage leg: what the mod REGISTERS against what the map CONTAINS.
    //
    // The check this joins up measured the generator against the generator —
    // SandboxTests.Coverage reads the [ScriptedEntity] attributes and looks for
    // them in Sandbox.Build(), which is the very thing that placed them. It can
    // report a gap only if the generator disagrees with itself. Between that
    // check and the gate sits vbsp, and NOTHING looked across it: when this was
    // written the compiled ss_sandbox.bsp was 72 classnames behind the
    // generator, env_smokestack among them, and both checks were green.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARegisteredClassWithNoInstanceInTheMapFailsTheCoverageLeg()
    {
        var contents = new[] { Ent("prop_dynamic", "sign") };

        var absences = SandboxPass.AuditCoverage(
            new[] { "prop_dynamic", "env_smokestack" }, contents);

        Assert.Single(absences);
        Assert.Equal("env_smokestack", absences[0].ClassName);
        Assert.True(absences[0].Fails, "a class the mod registers and the map has none of fails");
        Assert.Null(absences[0].Exemption);
    }

    [Fact]
    public void OneInstanceIsEnoughForTheCoverageLeg()
    {
        // The rule is presence, not a count. How many of a class the server
        // actually built out of what the map declared is Audit's question, and
        // scoring it twice would make one number answer for two failures.
        var contents = new[] { Ent("func_door", "a") };

        Assert.Empty(SandboxPass.AuditCoverage(new[] { "func_door" }, contents));
    }

    [Fact]
    public void TheCoverageLegIsCaseInsensitiveAboutClassnames()
    {
        // A .bsp holds whatever case the mapper typed; the attribute holds
        // whatever the C# constant says. A coverage rule that failed on case
        // would be turned off within a day.
        var contents = new[] { Ent("Prop_Dynamic", "sign") };

        Assert.Empty(SandboxPass.AuditCoverage(new[] { "prop_dynamic" }, contents));
    }

    [Fact]
    public void AnExemptClassIsReportedRatherThanFilteredOut()
    {
        // Named, with its reason, and still in the list. An exemption that
        // vanishes from the report is a list nobody reads and therefore nobody
        // disagrees with, which is how a real regression gets absorbed.
        var absences = SandboxPass.AuditCoverage(
            new[] { SourceSharp.Shared.EntityNames.SsGrenade },
            Array.Empty<SandboxPass.DeclaredEntity>());

        Assert.Single(absences);
        Assert.False(absences[0].Fails, "an exempt class is absent without failing");
        Assert.NotNull(absences[0].Exemption);
        Assert.Contains("weapon_frag", absences[0].Exemption!);
    }

    [Fact]
    public void TheCompilersOwnConsumedClassesAreExemptByName()
    {
        // func_detail and its eight relatives never reach a compiled entity
        // lump: vbsp clears their keyvalues (src/utils/vbsp/map.cpp). No class
        // this mod implements is one of them TODAY, so without this test those
        // nine entries would be lines nothing exercises.
        foreach (string consumed in new[]
                 {
                     "func_detail", "func_viscluster", "env_cubemap", "info_overlay",
                     "info_overlay_transition", "info_no_dynamic_shadow", "func_instance",
                     "func_instance_parms", "func_ladder",
                 })
        {
            var absences = SandboxPass.AuditCoverage(
                new[] { consumed }, Array.Empty<SandboxPass.DeclaredEntity>());

            Assert.Single(absences);
            Assert.False(absences[0].Fails,
                $"{consumed} is absent because vbsp ate it, not because the map is short");
            Assert.Contains("vbsp", absences[0].Exemption!);
        }
    }

    [Fact]
    public void EveryExemptionCarriesAReasonSomebodyCanDisagreeWith()
    {
        // The bar for the table is that a reader can argue with each entry. An
        // empty string, or a single word, would pass a "has an exemption" check
        // and say nothing.
        foreach (var pair in SandboxPass.Exemptions)
        {
            Assert.True(pair.Value.Length > 40,
                $"the exemption for {pair.Key} explains itself ({pair.Value.Length} characters)");
        }
    }

    /// <summary>
    /// The mod's own registry against the map the generator would produce.
    ///
    /// This is the .vmf half of the coverage rule and it is the half `make test`
    /// can reach: compiling the .bsp needs Wine, a Steam depot and the HL2
    /// content, which docs/sandbox.md deliberately keeps out of the suite. So
    /// the suite proves the GENERATOR is complete, and `MapGen mapcheck` — run
    /// by `make map` the moment a map compiles, and by tools/sandbox-pass
    /// against the running game's own .bsp — proves the compiled map is.
    /// </summary>
    [Fact]
    public void TheGeneratorPlacesEveryClassTheModRegisters()
    {
        var map = Sandbox.Build();

        var contents = map.Entities
            .Select(e => new SandboxPass.DeclaredEntity(
                e.ClassName, e.Name, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        var registered = SandboxPass.Registered();

        Assert.True(registered.Count > 250, $"the mod registers entities to demonstrate ({registered.Count})");

        var missing = SandboxPass.AuditCoverage(registered, contents)
            .Where(a => a.Fails)
            .Select(a => a.ClassName)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"the generator places every class the mod registers; missing: {string.Join(", ", missing)}");
    }

    //-------------------------------------------------------------------------
    // Reading a .bsp's entity lump. The gate used to get this through debug-api and
    // therefore only with a game running; mapcheck reads it off disk.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheEntityLumpParsesIntoClassnamesAndKeys()
    {
        const string lump = "{\n\"classname\" \"worldspawn\"\n\"skyname\" \"sky_day01_01\"\n}\n"
                          + "{\n\"origin\" \"0 0 64\"\n\"targetname\" \"chimney\"\n\"classname\" \"env_smokestack\"\n}\n";

        var entities = BspEntities.Parse(lump);

        Assert.Equal(2, entities.Count);
        Assert.Equal("env_smokestack", entities[1].ClassName);
        Assert.Equal("chimney", entities[1].TargetName);
        Assert.Equal("0 0 64", entities[1].KeyValues["origin"]);
    }

    [Fact]
    public void AnEntityBlockWithNoClassnameIsNotAnEntity()
    {
        // vbsp writes cleared entities as empty blocks. Counting one as an
        // entity with an empty classname would put a phantom in the report.
        var entities = BspEntities.Parse("{\n}\n{\n\"classname\" \"info_target\"\n}\n");

        Assert.Single(entities);
        Assert.Equal("info_target", entities[0].ClassName);
    }

    [Fact]
    public void AKeyvalueWhoseValueSpansLinesIsReadWhole()
    {
        // point_worldtext's "message" carries REAL newlines -- that is how the
        // entity draws more than one line. CPointWorldText::KeyValue does a
        // verbatim V_strncpy of the value (src/game/server/point_worldtext.cpp),
        // and the VMF tokenizer turns the two-character \n escape into a literal
        // LF (TokenReader::GetString, src/tier1/tokenreader.cpp), so vbsp writes
        // a quoted string with an LF inside it into the entity lump. That is a
        // correct .bsp.
        //
        // A reader that splits the lump on '\n' and parses each line on its own
        // sees an opening quote with no closing quote, drops the key, and
        // answers "" for it. The staleness rule then reports the COMPILED MAP
        // as wrong when the map is right and the reader is wrong -- which is
        // what it did: a freshly compiled ss_sandbox.bsp came back two entities
        // "behind" the generator, both of them the multi-line worldtexts.
        const string lump = "{\n\"classname\" \"point_worldtext\"\n"
                          + "\"targetname\" \"notices_worldtext\"\n"
                          + "\"message\" \"orientation 0\nfixed to my angles\"\n"
                          + "\"textsize\" \"8\"\n}\n";

        var entities = BspEntities.Parse(lump);

        Assert.Single(entities);
        Assert.Equal("point_worldtext", entities[0].ClassName);
        Assert.Equal("notices_worldtext", entities[0].TargetName);
        Assert.Equal("orientation 0\nfixed to my angles", entities[0].KeyValues["message"]);

        // And the pairs AFTER the multi-line one still land in the right slots.
        // A parser that resynchronised by hunting for the next line beginning
        // with a quote would read "fixed to my angles" as a key and pair the
        // rest of the block off by one.
        Assert.Equal("8", entities[0].KeyValues["textsize"]);
    }

    [Fact]
    public void ABraceInsideAQuotedValueDoesNotEndTheEntity()
    {
        // The other half of scanning rather than splitting: once a value is a
        // span between quotes, a brace inside one is text. A line-based reader
        // never had to think about it because it never saw a value it could not
        // close; a scanner does.
        const string lump = "{\n\"classname\" \"point_worldtext\"\n"
                          + "\"message\" \"a } and a { in the sign\"\n"
                          + "\"textsize\" \"8\"\n}\n";

        var entities = BspEntities.Parse(lump);

        Assert.Single(entities);
        Assert.Equal("a } and a { in the sign", entities[0].KeyValues["message"]);
        Assert.Equal("8", entities[0].KeyValues["textsize"]);
    }

    [Fact]
    public void AFileThatIsNotABspIsRefusedRatherThanReadAsEmpty()
    {
        // The failure this prevents: a reader that answers "no entities" for a
        // file it did not understand turns every coverage failure into a pass,
        // with no way to tell the two apart.
        string path = Path.Combine(Path.GetTempPath(), $"a13d-not-a-bsp-{Guid.NewGuid():N}.bsp");

        try
        {
            File.WriteAllBytes(path, new byte[8 + (64 * 16)]);
            Assert.Throws<InvalidDataException>(() => BspEntities.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
