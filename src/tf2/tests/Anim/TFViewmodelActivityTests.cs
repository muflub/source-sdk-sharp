using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Tests.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Anim;

/// <summary>
/// The viewmodel act table (s_viewmodelacttable,
/// tf_weaponbase.cpp:4292) as the chain's rule: the base activity the machine
/// sends renamed to the weapon role's own before the model is asked.
/// </summary>
public sealed class TFViewmodelActivityTests
{
    /// <summary>A role without the script read, for the table's own facts.</summary>
    private sealed class RoleSource(TFWeaponRole? role) : ITFAnimWeapons
    {
        public TFAnimWeapon? ActiveWeapon(int playerIndex)
            => role is { } r ? new TFAnimWeapon(r, TFWeaponId.Rocketlauncher) : null;

        public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
    }

    private static TFViewmodelActivityRule Rule(TFWeaponRole? role) => new(new RoleSource(role));

    private const int Player = 3;

    [Theory]
    [InlineData(TFWeaponRole.Primary, "ACT_PRIMARY_VM_IDLE")]
    [InlineData(TFWeaponRole.Secondary, "ACT_SECONDARY_VM_IDLE")]
    [InlineData(TFWeaponRole.Melee, "ACT_MELEE_VM_IDLE")]
    public void The_idle_renames_to_the_roles_own(TFWeaponRole role, string wanted)
        => Assert.Equal(wanted, Rule(role).Translate(Player, WeaponAnim.Idle, "ACT_VM_IDLE"));

    [Fact]
    public void The_melee_hit_renames_through_the_role_too()
    {
        // The machine's first miss-candidate (ACT_VM_HITCENTER, the crowbar's
        // fallback) is a base activity the melee table renames; a model asked
        // with the unrenamed name would answer the HL2 crowbar's or nothing.
        Assert.Equal("ACT_MELEE_VM_HITCENTER",
                     Rule(TFWeaponRole.Melee).Translate(Player, WeaponAnim.Primary, "ACT_VM_HITCENTER"));
    }

    [Fact]
    public void A_base_the_table_does_not_name_stands()
    {
        // ACT_VM_THROW (the grenade's), ACT_VM_MISSCENTER (a swing's miss) and
        // the HL2 shotgun set have no rows: stock's table-miss arm is the base.
        var rule = Rule(TFWeaponRole.Primary);
        Assert.Equal("ACT_VM_THROW", rule.Translate(Player, WeaponAnim.Primary, "ACT_VM_THROW"));
        Assert.Equal("ACT_VM_MISSCENTER", rule.Translate(Player, WeaponAnim.Miss, "ACT_VM_MISSCENTER"));
        Assert.Equal("ACT_SHOTGUN_PUMP", rule.Translate(Player, WeaponAnim.Pump, "ACT_SHOTGUN_PUMP"));
        Assert.Equal("ACT_SHOTGUN_RELOAD_START", rule.Translate(Player, WeaponAnim.ReloadStart, "ACT_SHOTGUN_RELOAD_START"));
    }

    [Fact]
    public void A_role_with_no_rows_leaves_every_name()
    {
        // GRENADE, HEAD, MISC, PASSTIME_BALL and BUILDING have no viewmodel
        // rows; their block is stock's table-miss arm.
        foreach (string activity in new[] { "ACT_VM_IDLE", "ACT_VM_DRAW", "ACT_VM_PRIMARYATTACK", "ACT_VM_RELOAD" })
            Assert.Equal(activity, Rule(TFWeaponRole.Grenade).Translate(Player, WeaponAnim.Idle, activity));
    }

    [Fact]
    public void No_weapon_leaves_the_base_name()
    {
        // Stock's gate: no owner, no weapon, an unnamed weapon — the rewrite
        // is never reached (tf_weaponbase.cpp:4515).
        foreach (string activity in WeaponAnimNames.ActivitiesFor(WeaponAnim.Idle))
            Assert.Equal(activity, Rule(null).Translate(Player, WeaponAnim.Idle, activity));
    }

    //-------------------------------------------------------------------------
    // End to end through the weapon scripts: the role read is the shipped
    // WeaponType (GetViewModelWeaponRole's base arm).
    //-------------------------------------------------------------------------

    private static TFViewmodelActivityRule ScriptedRule(RecordingEngine engine)
        => new(new TFServerAnimWeapons(engine,
                                       new TFWeaponScripts(new RecordingLog(), WeaponFixtures.Shipped())));

    [Fact]
    public void The_rocket_launcher_renames_the_idle_to_the_primary_set()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_rocketlauncher");

        var rule = ScriptedRule(engine);

        Assert.Equal("ACT_PRIMARY_VM_IDLE", rule.Translate(PlayerRig.Index, WeaponAnim.Idle, "ACT_VM_IDLE"));
        Assert.Equal("ACT_PRIMARY_VM_DRAW", rule.Translate(PlayerRig.Index, WeaponAnim.Draw, "ACT_VM_DRAW"));
        Assert.Equal("ACT_PRIMARY_VM_PRIMARYATTACK", rule.Translate(PlayerRig.Index, WeaponAnim.Primary, "ACT_VM_PRIMARYATTACK"));
        Assert.Equal("ACT_PRIMARY_VM_RELOAD", rule.Translate(PlayerRig.Index, WeaponAnim.Reload, "ACT_VM_RELOAD"));
    }

    [Fact]
    public void The_shotgun_renames_the_idle_to_the_secondary_set()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_shotgun_soldier");

        Assert.Equal("ACT_SECONDARY_VM_IDLE",
                     ScriptedRule(engine).Translate(PlayerRig.Index, WeaponAnim.Idle, "ACT_VM_IDLE"));
    }

    [Fact]
    public void The_bat_renames_the_idle_to_the_melee_set()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_bat");

        Assert.Equal("ACT_MELEE_VM_IDLE",
                     ScriptedRule(engine).Translate(PlayerRig.Index, WeaponAnim.Idle, "ACT_VM_IDLE"));
    }

    //-------------------------------------------------------------------------
    // Stock's first arm, the item's animation_replacement
    // (tf_weaponbase.cpp:4515-4521): the heavy's fists rename the ask before
    // any role table, because c_heavy_arms tags no ACT_MELEE_* — without the
    // arm the idle misses onto the plain idle label and the fists hang at the
    // screen's bottom corners.
    //-------------------------------------------------------------------------

    private static TFViewmodelActivityRule ItemisedRule(RecordingEngine engine)
        => new(new TFServerAnimWeapons(engine,
                                       new TFWeaponScripts(new RecordingLog(), WeaponFixtures.Shipped())),
               WeaponFixtures.Types(EconTestFiles.Fixture("items_game.trimmed.txt")));

    [Fact]
    public void The_fists_rename_the_idle_through_their_item()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");

        Assert.Equal("ACT_FISTS_VM_IDLE",
                     ItemisedRule(engine).Translate(PlayerRig.Index, WeaponAnim.Idle, "ACT_VM_IDLE"));
    }

    [Fact]
    public void The_fists_item_rename_beats_the_role_table()
    {
        // Stock's early return: an item row is the answer, the role table is
        // never reached (tf_weaponbase.cpp:4519-4520) — the draw is the
        // FISTS one, not ACT_MELEE_VM_DRAW.
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");

        var rule = ItemisedRule(engine);

        Assert.Equal("ACT_FISTS_VM_DRAW", rule.Translate(PlayerRig.Index, WeaponAnim.Draw, "ACT_VM_DRAW"));
        Assert.Equal("ACT_FISTS_VM_SWINGHARD", rule.Translate(PlayerRig.Index, WeaponAnim.Primary, "ACT_VM_SWINGHARD"));
    }

    [Fact]
    public void A_weapon_whose_item_names_no_replacement_keeps_the_role_table()
    {
        // The rocket launcher's item has no animation_replacement: stock's
        // fall-through arm, the role table as before.
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_rocketlauncher");

        Assert.Equal("ACT_PRIMARY_VM_IDLE",
                     ItemisedRule(engine).Translate(PlayerRig.Index, WeaponAnim.Idle, "ACT_VM_IDLE"));
    }

    [Fact]
    public void An_item_row_for_an_unasked_activity_is_no_answer()
    {
        // The fists rename five activities; the secondary attack is not one
        // of them, so the melee table's row stands.
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", PlayerRig.Player);
        engine.Returns("Players.GetActiveWeapon", "tf_weapon_fists");

        Assert.Equal("ACT_MELEE_VM_SECONDARYATTACK",
                     ItemisedRule(engine).Translate(PlayerRig.Index, WeaponAnim.Secondary, "ACT_VM_SECONDARYATTACK"));
    }
}
