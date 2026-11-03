using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Tests.Entities.Items;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The sapper's use (<see cref="TFSapperUse"/>): the placement out of the
/// spy's slot (tf_obj.cpp:916-1168's hostile arm), the teleporter pair's
/// second sapper, the wrench's fix and the pair's shared blow, and the
/// links that carry them (<see cref="TFWrenchHook"/>, the <c>sap</c>
/// command).
/// </summary>
public sealed class TFSapperUseTests
{
    private const int Attack = (int)InputButtons.Attack;

    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    /// <summary>The eye the stub answers: a player at the origin, standing eye, facing +X.</summary>
    private static readonly Vector Eye = new(0f, 0f, 64f);

    /// <summary>The reach the wrench hook asks the seam with: <c>CTFWrench::Smack</c>'s 70 (tf_weapon_wrench.cpp:165).</summary>
    private const float Swing = TFWrenchHook.SmackRange;

    private static TFSapperUse Use(BuildingsRig rig) =>
        new(rig.Players, rig.Engine, rig.Engine, rig.Engine, rig.Buildings(), rig.Engine,
            rig.Scopes, rig.Spawner, rig.Log);

    /// <summary>
    /// The stubbed player world: every named player resolves from its own
    /// index, standing at the origin, eye at the standing height, facing +X.
    /// </summary>
    private static void StubEngine(BuildingsRig rig, params EntityHandle[] who)
    {
        rig.Engine.ReturnsEachCall("Players.GetHandle", () =>
        {
            int index = (rig.Engine.Last("Players.GetHandle") ?? default).ArgAs<int>(0);
            foreach (EntityHandle handle in who)
                if (handle.EntryIndex == index)
                    return handle;
            return EntityHandle.None;
        });
        rig.Engine.Returns("Players.GetShootOrigin", Eye);
        rig.Engine.Returns("Entities.GetEyeAngles", Vector.Zero);
        rig.Engine.ReturnsEachCall("Entities.GetOrigin", () => Vector.Zero);
    }

    /// <summary>A built sentry of <paramref name="team"/>, its centre where the fact puts it.</summary>
    private static EntityHandle ABuilt(BuildingsRig rig, TFTeam team, Vector center)
    {
        EntityHandle builder = rig.AddBuilder(team);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        rig.Clock.Now = 100f;
        sentry.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();
        rig.Centers[sentry.Handle] = center;
        return builder;
    }

    /// <summary>A built dispenser of <paramref name="team"/>, its centre where the fact puts it.</summary>
    private static EntityHandle ABuiltDispenser(BuildingsRig rig, TFTeam team, Vector center)
    {
        EntityHandle builder = rig.AddBuilder(team);
        var dispenser = rig.Spawn<TFDispenser>(TFDispenser.MapClass);
        dispenser.StartPlacement(builder);
        rig.Clock.Now = 100f;
        dispenser.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Dispenser)!.BuildTime;
        dispenser.OnThink();
        rig.Centers[dispenser.Handle] = center;
        return builder;
    }

    /// <summary>
    /// One sapped sentry: the red engineer's at (100, 0, 32), the blue spy's
    /// sapper riding it — its centre on the engineer's swing line — and both
    /// players at the origin facing +X.
    /// </summary>
    private static (BuildingsRig Rig, TFSapper Sapper, TFBaseObject Sentry, EntityHandle Engineer) ASappedBuilding()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle engineer = ABuilt(rig, TFTeam.Red, new Vector(100f, 0f, 32f));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, engineer, spy);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        TFSapper sapper = rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper).OfType<TFSapper>().Single();
        rig.Centers[sapper.Handle] = new Vector(40f, 0f, 64f);
        var sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        rig.Centers[sentry.Handle] = new Vector(60f, 0f, 64f);   // the building sits on the swing line
        return (rig, sapper, sentry, engineer);
    }

    /// <summary>One engineer's pair, built, both ends where the placement facts put them.</summary>
    private static (BuildingsRig Rig, TFTeleporter Entrance, TFTeleporter Exit, EntityHandle Builder) APair()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        var entrance = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);
        var exit = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);

        foreach (var (end, mode) in new[] { (entrance, 0), (exit, 1) })
        {
            end.StartPlacement(builder);
            end.SetObjectMode(mode);
            end.StartBuilding();
        }

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Teleporter)!.BuildTime;
        entrance.OnThink();
        exit.OnThink();

        rig.Centers[entrance.Handle] = new Vector(100f, 0f, 32f);
        rig.Centers[exit.Handle] = new Vector(90f, 40f, 32f);
        return (rig, entrance, exit, builder);
    }

    /// <summary>The sappers the placement left, by the building they ride.</summary>
    private static TFSapper SapperOn(BuildingsRig rig, TFBaseObject building) =>
        rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper).OfType<TFSapper>()
           .Single(s => s.BuiltOn == building.Handle);

    //---------------------------------------------------------------------
    // The placement
    //---------------------------------------------------------------------

    [Fact]
    public void The_sapper_slot_plants_on_the_enemy_building_in_front()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Red, new Vector(100f, 0f, 32f));
        var sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        TFSapper sapper = SapperOn(rig, sentry);
        Assert.Equal(sentry.Handle, sapper.BuiltOn);
        Assert.True(sentry.HasSapper);   // riding: the instant build's OnAddSapper
        Assert.Equal(new Vector(100f, 0f, 32f), rig.Spawner.Placed[sapper.Handle]);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("obj_sentrygun sapped", StringComparison.Ordinal));
    }

    [Fact]
    public void The_sapper_slot_refuses_a_friendly_building()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Blue, new Vector(100f, 0f, 32f));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);

        Assert.False(Use(rig).PlaceSapper(spy.EntryIndex));
        Assert.Empty(rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper));
    }

    [Fact]
    public void The_sapper_slot_refuses_an_already_sapped_building()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Red, new Vector(100f, 0f, 32f));
        var sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));
        Assert.False(Use(rig).PlaceSapper(spy.EntryIndex));   // one sapper a building

        Assert.Single(rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper));
    }

    [Fact]
    public void The_sapper_slot_refuses_a_building_beyond_the_attach_distance()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Red, new Vector(200f, 0f, 32f));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);

        Assert.False(Use(rig).PlaceSapper(spy.EntryIndex));
        Assert.Empty(rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper));
    }

    [Fact]
    public void The_sapper_slot_refuses_a_building_outside_the_view_cone()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        // Beside the spy: the flat dot is 0.707, under the 0.766 cone.
        EntityHandle builder = ABuilt(rig, TFTeam.Red, new Vector(100f, 100f, 32f));
        // Behind him: the dot is -1.
        EntityHandle behind = ABuilt(rig, TFTeam.Red, new Vector(-100f, 0f, 32f));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, behind, spy);

        Assert.False(Use(rig).PlaceSapper(spy.EntryIndex));
        Assert.Empty(rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper));
    }

    [Fact]
    public void A_wall_between_ends_the_search()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Red, new Vector(100f, 0f, 32f));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);
        rig.BlockedEnds.Add(new Vector(100f, 0f, 32f));   // the eye's line stops short of the building

        Assert.False(Use(rig).PlaceSapper(spy.EntryIndex));
        Assert.Empty(rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper));
    }

    [Fact]
    public void The_nearest_enemy_building_wins()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Red, new Vector(100f, 0f, 32f));
        EntityHandle farther = ABuilt(rig, TFTeam.Red, new Vector(150f, 0f, 32f));
        var near = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun)
                      .Single(b => RigVectors.Same(rig.Centers[b.Handle], new Vector(100f, 0f, 32f)));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, farther, spy);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        Assert.Single(rig.Buildings().OwnedBy(type: TFObjectType.AttachmentSapper));
        Assert.Equal(near.Handle, SapperOn(rig, near).BuiltOn);
    }

    [Fact]
    public void A_sapped_teleporter_ends_up_sapped_at_both_ends()
    {
        var (rig, entrance, exit, builder) = APair();
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        Assert.True(entrance.HasSapper);
        Assert.True(exit.HasSapper);
        SapperOn(rig, entrance);
        SapperOn(rig, exit);
    }

    [Fact]
    public void A_still_building_exit_takes_its_pair_sapper()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        var entrance = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);
        var exit = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);

        entrance.StartPlacement(builder);
        entrance.SetObjectMode(0);
        entrance.StartBuilding();
        exit.StartPlacement(builder);
        exit.SetObjectMode(1);
        exit.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Teleporter)!.BuildTime;
        entrance.OnThink();   // the entrance finished; the exit has not thought

        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);
        rig.Centers[entrance.Handle] = new Vector(80f, 0f, 32f);
        rig.Centers[exit.Handle] = new Vector(100f, 0f, 32f);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        // Stock's FindMatch gates on nothing built (tf_obj_teleporter.cpp
        // :1282-1326): the still-building exit holds its half of the pair.
        Assert.True(entrance.HasSapper);
        Assert.True(exit.HasSapper);
        SapperOn(rig, entrance);
        SapperOn(rig, exit);
    }

    //---------------------------------------------------------------------
    // The wrench's fix
    //---------------------------------------------------------------------

    [Fact]
    public void A_sapper_takes_only_the_wrenchs_fix()
    {
        var (rig, sapper, _, engineer) = ASappedBuilding();

        // No scope: nothing lands.
        Assert.Equal(0, BuildingsRig.Hit(sapper, engineer, TFSapperUse.WrenchDmgVsSapper, DamageType.Club));
        Assert.Equal(TFSapper.SapperMaxHealth, sapper.Health);

        // A wrong scope: nothing lands either.
        using (rig.Scopes.Push(TFDamageCustom.Backstab, TFWeaponId.Knife, TFKnifeHook.KnifeScript))
            Assert.Equal(0, BuildingsRig.Hit(sapper, engineer, TFSapperUse.WrenchDmgVsSapper, DamageType.Club));
        Assert.Equal(TFSapper.SapperMaxHealth, sapper.Health);

        // The wrench's fix: taken.
        using (rig.Scopes.Push(TFDamageCustom.WrenchFix, TFWeaponId.Wrench, TFWrenchHook.WrenchScript))
            Assert.Equal(TFSapperUse.WrenchDmgVsSapper,
                         BuildingsRig.Hit(sapper, engineer, TFSapperUse.WrenchDmgVsSapper, DamageType.Club));
        Assert.Equal(TFSapper.SapperMaxHealth - TFSapperUse.WrenchDmgVsSapper, sapper.Health);
    }

    [Fact]
    public void The_wrenchs_smack_hurts_the_sapper_on_a_friendly_building()
    {
        var (rig, sapper, _, engineer) = ASappedBuilding();

        Assert.True(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));

        var hit = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(sapper.Handle, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(engineer, hit.ArgAs<EntityHandle>(1));
        Assert.Equal(65f, hit.ArgAs<float>(2));   // WRENCH_DMG_VS_SAPPER
        Assert.Equal(DamageType.Club, hit.ArgAs<DamageType>(3));
    }

    [Fact]
    public void The_smack_lands_on_the_nearest_sapper_the_swing_reaches()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle engineer = ABuilt(rig, TFTeam.Red, new Vector(100f, 0f, 32f));
        EntityHandle dispenserBuilder = ABuiltDispenser(rig, TFTeam.Red, new Vector(80f, 0f, 32f));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, engineer, dispenserBuilder, spy);

        // Two sapped buildings: the placement takes the nearest first, then
        // skips it for the farther one.
        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));
        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        TFDispenser dispenser = rig.Buildings().OwnedBy(type: TFObjectType.Dispenser).OfType<TFDispenser>().Single();
        TFSentryGun sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        TFSapper onDispenser = SapperOn(rig, dispenser);
        TFSapper onSentry = SapperOn(rig, sentry);
        rig.Centers[dispenser.Handle] = new Vector(40f, 0f, 64f);   // the nearer, on the swing line
        rig.Centers[sentry.Handle] = new Vector(60f, 0f, 64f);      // the farther, also on it

        Assert.True(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));

        // Stock's trace lands the blow on the nearest hit along the swing
        // (tf_weapon_wrench.cpp:170-186) — the dispenser's sapper at 40, not
        // the sentry's at 60 that enumeration order used to hand it.
        var hit = Assert.Single(rig.Engine.AllOf("Damage.TakeDamage"));
        Assert.Equal(onDispenser.Handle, hit.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void The_wrench_leaves_an_enemy_sapper_alone()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Blue, new Vector(100f, 0f, 32f));
        var sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        EntityHandle spy = rig.AddBuilder(TFTeam.Red, TFClass.Spy);   // the red spy saps the blue sentry
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        StubEngine(rig, builder, spy, engineer);

        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));
        rig.Centers[SapperOn(rig, sentry).Handle] = new Vector(40f, 0f, 64f);

        // The building is the engineer's enemy: OnFriendlyBuildingHit names
        // nothing.
        Assert.False(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));
    }

    [Fact]
    public void The_wrench_leaves_the_enemy_building_alone()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = ABuilt(rig, TFTeam.Blue, new Vector(60f, 0f, 64f));
        EntityHandle engineer = rig.AddBuilder(TFTeam.Red);
        StubEngine(rig, builder, engineer);

        // The building is the engineer's enemy: OnFriendlyBuildingHit names
        // nothing, sapper or no.
        Assert.False(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));
    }

    [Fact]
    public void The_smack_repairs_the_wounded_building_under_it()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle engineer = ABuilt(rig, TFTeam.Red, new Vector(60f, 0f, 64f));   // on the swing line
        var sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        BuildingsRig.Hit(sentry, enemy, 30f, DamageType.Bullet);   // 150 → 120
        StubEngine(rig, engineer);
        rig.Purse.Pockets[MetalName] = 200;

        Assert.True(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));

        // OnWrenchHit's repair (tf_obj.cpp:2807-2822): 30 health for 10 metal.
        Assert.Equal(150, sentry.Health);
        Assert.Equal(190, rig.Purse.Pockets[MetalName]);
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));   // no sapper rode: no blow
    }

    [Fact]
    public void The_smack_pours_the_upgrade_metal_into_a_whole_one()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle engineer = ABuilt(rig, TFTeam.Red, new Vector(60f, 0f, 64f));
        var sentry = rig.Buildings().OwnedBy(type: TFObjectType.Sentrygun).OfType<TFSentryGun>().Single();
        StubEngine(rig, engineer);
        rig.Purse.Pockets[MetalName] = 200;

        Assert.True(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));

        // Whole and unsapped: the swing's metal goes onto the level's pot
        // (CheckUpgradeOnHit, tf_obj.cpp:2846-2914).
        Assert.Equal(25, sentry.UpgradeMetal);
        Assert.Equal(175, rig.Purse.Pockets[MetalName]);
    }

    [Fact]
    public void A_sapper_beyond_the_swing_is_unhurt()
    {
        var (rig, sapper, sentry, engineer) = ASappedBuilding();
        rig.Centers[sentry.Handle] = new Vector(40f, 200f, 64f);   // beside the swing line, not on it

        Assert.False(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));
    }

    [Fact]
    public void A_wall_before_the_sapper_stops_the_smack()
    {
        var (rig, sapper, sentry, engineer) = ASappedBuilding();
        // Just past the swing's end, still inside the hull — and the wall
        // the swing's line stops on, short of it.
        rig.Centers[sentry.Handle] = new Vector(Swing + 2f, 0f, 64f);
        rig.BlockedEnds.Add(Eye + new Vector(Swing, 0f, 0f));

        Assert.False(Use(rig).WrenchSmack(engineer.EntryIndex, Swing));
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));
    }

    [Fact]
    public void The_pair_s_sapper_takes_the_same_blow()
    {
        var (rig, entrance, exit, builder) = APair();
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        StubEngine(rig, builder, spy);
        Assert.True(Use(rig).PlaceSapper(spy.EntryIndex));

        TFSapper first = SapperOn(rig, entrance);
        TFSapper second = SapperOn(rig, exit);
        rig.Centers[entrance.Handle] = new Vector(40f, 0f, 64f);   // on the swing line
        rig.Centers[exit.Handle] = new Vector(500f, 300f, 64f);   // well beyond it

        Assert.True(Use(rig).WrenchSmack(builder.EntryIndex, Swing));

        var hits = rig.Engine.AllOf("Damage.TakeDamage").ToList();
        Assert.Equal(2, hits.Count);
        Assert.All(hits, hit => Assert.Equal(65f, hit.ArgAs<float>(2)));   // WRENCH_DMG_VS_SAPPER
        Assert.All(hits, hit => Assert.Equal(builder, hit.ArgAs<EntityHandle>(1)));
        Assert.Equal(new HashSet<EntityHandle> { first.Handle, second.Handle },
                     hits.Select(hit => hit.ArgAs<EntityHandle>(0)).ToHashSet());
    }

    [Fact]
    public void The_wrenchs_fix_kills_the_sapper_and_frees_the_building()
    {
        var (rig, sapper, sentry, engineer) = ASappedBuilding();

        using (rig.Scopes.Push(TFDamageCustom.WrenchFix, TFWeaponId.Wrench, TFWrenchHook.WrenchScript))
        {
            BuildingsRig.Hit(sapper, engineer, TFSapperUse.WrenchDmgVsSapper, DamageType.Club);
            BuildingsRig.Hit(sapper, engineer, TFSapperUse.WrenchDmgVsSapper, DamageType.Club);
        }

        Assert.True(sapper.IsDead);
        Assert.False(sentry.HasSapper);
        Assert.True(sentry.IsDisabled);   // the SAPPER_REMOVE_DISABLE_TIME stumble
    }

    //---------------------------------------------------------------------
    // The links: the wrench hook and the sap command
    //---------------------------------------------------------------------

    /// <summary>The sapper seam as a recording, for the hook and command facts.</summary>
    private sealed class RecordingSappers : ITFSapperState
    {
        public List<(int Owner, float Range)> Smacks { get; } = new();
        public int Placements { get; private set; }

        public bool PlaceSapper(int ownerIndex)
        {
            Placements++;
            return true;
        }

        public bool WrenchSmack(int ownerIndex, float range)
        {
            Smacks.Add((ownerIndex, range));
            return true;
        }
    }

    [Fact]
    public void The_wrenchs_swing_asks_the_sapper_seam()
    {
        var sappers = new RecordingSappers();
        var world = new TFWeaponWorld(new TFWrenchHook(sappers));
        var (w, vm) = world.Armed("tf_weapon_wrench", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);
        world.Run(w, vm, 0, now: 10.115f);   // the smack asks nothing: the hit went at the swing

        Assert.Equal(new[] { (TFWeaponWorld.Owner, TFWrenchHook.SmackRange) }, sappers.Smacks);
    }

    [Fact]
    public void No_other_weapon_asks_the_sapper_seam()
    {
        var sappers = new RecordingSappers();
        var world = new TFWeaponWorld(new TFWrenchHook(sappers));
        var (w, vm) = world.Armed("tf_weapon_bat", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: 1);

        Assert.Empty(sappers.Smacks);
    }

    [Fact]
    public void The_sap_command_asks_the_seam()
    {
        var sappers = new RecordingSappers();
        var rig = new BuildingsRig();
        (EntityHandle spy, _) = rig.Players.Add(TFTeam.Blue, TFClass.Spy);
        var command = new TFSapperCommand(sappers, rig.Players, rig.Log);

        Assert.True(command.Handle(spy, "sap", ReadOnlySpan<string>.Empty));
        Assert.Equal(1, sappers.Placements);
    }

    [Fact]
    public void The_sap_command_passes_other_verbs_through()
    {
        var sappers = new RecordingSappers();
        var rig = new BuildingsRig();
        (EntityHandle spy, _) = rig.Players.Add(TFTeam.Blue, TFClass.Spy);
        var command = new TFSapperCommand(sappers, rig.Players, rig.Log);

        Assert.False(command.Handle(spy, "build", ReadOnlySpan<string>.Empty));
        Assert.Equal(0, sappers.Placements);
    }

    [Fact]
    public void The_sap_command_refuses_a_player_who_is_not_a_spy()
    {
        var sappers = new RecordingSappers();
        var rig = new BuildingsRig();
        (EntityHandle soldier, _) = rig.Players.Add(TFTeam.Blue, TFClass.Soldier);
        var command = new TFSapperCommand(sappers, rig.Players, rig.Log);

        // Stock reaches the placement only through the spy's tf_weapon_sapper
        // (CTFWeaponBuilder::PrimaryAttack, tf_weapon_builder.cpp:328-341):
        // no other class holds one.
        Assert.True(command.Handle(soldier, "sap", ReadOnlySpan<string>.Empty));
        Assert.Equal(0, sappers.Placements);
    }

    [Fact]
    public void The_sap_command_refuses_a_dead_spy()
    {
        var sappers = new RecordingSappers();
        var rig = new BuildingsRig();
        (EntityHandle spy, FakeMapPlayer state) = rig.Players.Add(TFTeam.Blue, TFClass.Spy);
        state.Alive = false;
        var command = new TFSapperCommand(sappers, rig.Players, rig.Log);

        Assert.True(command.Handle(spy, "sap", ReadOnlySpan<string>.Empty));
        Assert.Equal(0, sappers.Placements);
    }
}
