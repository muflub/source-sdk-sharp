using System.Reflection;
using SourceSharp.Ai.Hl2mp.Knowledge;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Tests.Hl2mpNav;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Knowledge;

/// <summary>
/// What a bot knows before it plays (plan_hl2mp_ai §5.0): the weapon table,
/// diffed against the weapon scripts the game ships and against the
/// behaviours' own code for which reloads are heard; and the map's item
/// spawns, exactly the entity lump's.
/// </summary>
public sealed class WeaponKnowledgeTests
{
    private static readonly WeaponCatalog Catalog = WeaponCatalog.Stock();
    private static readonly WeaponKnowledge Facts = WeaponKnowledgeBuilder.Build(Catalog, _ => 1.5f);

    private sealed class Log : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private static AltFireFacts Alt(string weapon) => Facts.All.Single(f => f.Name == weapon).Alt;

    /// <summary>What each second trigger is, from the row and the classes it launches (§5.4): nothing assumed of a weapon that has none.</summary>
    [Theory]
    [InlineData("weapon_smg1", AltFireKind.Lob)]
    [InlineData("weapon_ar2", AltFireKind.Ball)]
    [InlineData("weapon_shotgun", AltFireKind.Double)]
    [InlineData("weapon_frag", AltFireKind.Underhand)]
    [InlineData("weapon_crossbow", AltFireKind.None)]
    [InlineData("weapon_357", AltFireKind.None)]
    [InlineData("weapon_pistol", AltFireKind.None)]
    [InlineData("weapon_rpg", AltFireKind.None)]
    [InlineData("weapon_crowbar", AltFireKind.None)]
    [InlineData("weapon_stunstick", AltFireKind.None)]
    [InlineData("weapon_physcannon", AltFireKind.None)]
    public void EachSecondTriggerIsWhatItsWeaponSays(string weapon, AltFireKind kind) => Assert.Equal(kind, Alt(weapon).Kind);

    [Fact]
    public void TheGrenadesNumbersAreItsClasss()
    {
        AltFireFacts g = Alt("weapon_smg1");
        Assert.Equal((1000f, GrenadeAr2.GravityUnits, new GrenadeAr2Vars().Radius, new GrenadeAr2Vars().Magnitude, 1, false), (g.Speed, g.Gravity, g.BlastRadius, g.Damage, g.AmmoCost, g.FromClip));
    }

    /// <summary>The ball: flat, no blast, and what it takes is the class's (no blast, a kill on what it touches).</summary>
    [Fact]
    public void TheBallIsFlatWithNoBlast()
    {
        AltFireFacts b = Alt("weapon_ar2");
        Assert.Equal((1000f, 0f, 0f, new EnergyBallVars().Damage), (b.Speed, b.Gravity, b.BlastRadius, b.Damage));
    }

    [Fact]
    public void BothBarrelsPayTwoFromTheClip()
    {
        AltFireFacts d = Alt("weapon_shotgun");
        Assert.Equal((2, true), (d.AmmoCost, d.FromClip));
    }

    [Fact]
    public void EveryClipAndRefireIsTheWeaponTablesAndTheScriptsAgree()
    {
        var scripts = new WeaponScriptDatabase(new Log(), p =>
        {
            string f = RepoLayout.Repo("game", "mod_test", p);
            return File.Exists(f) ? File.ReadAllText(f) : null;
        });
        int checkedScripts = 0;
        foreach (WeaponFacts f in Facts.All)
        {
            WeaponType w = Catalog.ByIndex(f.Kind)!.Value;
            Assert.Equal((w.ClipSize, w.Interval), (f.ClipSize, f.CadenceSeconds));
            if (scripts.ReadScript(f.Name) is { } script && f.HasClip)
            {
                Assert.True(script.MaxClip1 == f.ClipSize, $"{f.Name}: the script's clip_size {script.MaxClip1}, the table's {f.ClipSize}");
                checkedScripts++;
            }
        }

        Assert.True(checkedScripts >= 5, $"only {checkedScripts} scripts were read");
    }

    [Fact]
    public void TheHeardReloadsAreThoseWhoseBehaviourPlaysTheReloadSoundOnTheServer()
    {
        // The behaviours' IL: a method that reads the row's ReloadSound and hands it to IWeaponArms.Sound (not ClientSound).
        var fromIl = new HashSet<string>();
        foreach (KeyValuePair<string, IWeaponBehaviour> b in Hl2mpWeaponBehaviours.All())
        {
            foreach (MethodInfo m in b.Value.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                string[] calls = Callees(m);
                if (calls.Contains("WeaponType::get_ReloadSound") && calls.Contains("IWeaponArms::Sound"))
                    fromIl.Add(b.Key);
            }
        }

        string[] audible = [.. Hl2mpWeaponBehaviours.All().Select(b => b.Key).Where(WeaponKnowledgeBuilder.AudibleReload).Order()];
        Assert.Equal(fromIl.Order(), audible);
        Assert.Equal(4, audible.Length);
        Assert.False(Facts.All.Single(f => f.Name == "weapon_357").ReloadAudible);
        Assert.False(Facts.All.Single(f => f.Name == "weapon_crossbow").ReloadAudible, "the crossbow's reload is its owner's client's alone");
        Assert.False(Facts.All.Single(f => f.Name == "weapon_ar2").ReloadAudible, "the AR2's too");
        Assert.True(Facts.All.Single(f => f.Name == "weapon_smg1").ReloadAudible);
        Assert.True(Facts.All.Single(f => f.Name == "weapon_shotgun").ShellByShell);
    }

    private static string[] Callees(MethodInfo m)
    {
        byte[]? il = m.GetMethodBody()?.GetILAsByteArray();
        var found = new List<string>();
        if (il is null)
            return [];
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
                continue;
            try
            {
                if (m.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)) is { } callee)
                    found.Add($"{callee.DeclaringType?.Name}::{callee.Name}");
            }
            catch (ArgumentException)
            {
            }
        }

        return [.. found];
    }

    [PerTreeNavFact("dm_lockdown")]
    public void DmLockdownsItemSpawnsAreExactlyTheLumps()
    {
        MapEntities lump = MapEntities.Read(File.ReadAllBytes(PerTreeNav.BspPath("dm_lockdown")));
        MapKnowledge map = MapKnowledgeBuilder.Build("dm_lockdown", lump, Catalog, Facts, 20f, 30f);
        MapEntity[] placed = [.. lump.All.Where(e => e.ClassName.StartsWith("weapon_", StringComparison.Ordinal) || e.ClassName.StartsWith("item_", StringComparison.Ordinal))];
        Assert.Equal(placed.Select(e => (e.ClassName, e.Origin)), map.Items.Select(i => (i.ClassName, i.Position)));
        Assert.Equal(18, map.Items.Count(i => i.Kind == ItemKind.Weapon));
        Assert.Equal(9, map.Spawns.Length);
        Assert.All(map.Items.Where(i => i.Kind == ItemKind.Weapon), i => Assert.Equal(20f, i.RespawnSeconds));
    }
}
