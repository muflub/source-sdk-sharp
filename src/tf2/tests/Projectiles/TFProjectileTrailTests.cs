extern alias client;

using SourceSharp.Abi;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Projectiles;
using TF2.Tests.Projectiles;

using Xunit;

// The client's half, under the alias: the shared source compiles into both
// sides, so the mirrors and their netstates exist twice.
using CAbi = client::SourceSharp.Abi;
using ClientRocket = client::TF2.Client.Projectiles.ClientTFRocket;
using ClientPipe = client::TF2.Client.Projectiles.ClientTFPipebomb;
using ClientSticky = client::TF2.Client.Projectiles.ClientTFStickybomb;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The projectile trails, both halves: what the server writes when the
/// launcher arms (or a deflection retargets) the shot, and what system the
/// client's mirror starts on decoding it. The round trip is the netstate
/// blob — the shape the game runs.
/// </summary>
public sealed class TFProjectileTrailTests
{
    // =====================================================================
    // The round trip.
    // =====================================================================

    [Fact]
    public void The_rocket_mirror_starts_the_server_s_trail()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.State.BindLocal();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 3);

        var client = new TrailRig();
        client.Rocket(rocket.State.Snapshot());

        var start = client.Engine.Last("Effects.ParticleSystemStart");
        Assert.NotNull(start);
        Assert.Equal("rockettrail", start!.Value.ArgAs<string>(1));
        // Point 0 is the one a trail rides.
        Assert.Equal(0b1, client.Engine.Last("Effects.ParticleSystemFollowPoints")!.Value.ArgAs<int>(1));
    }

    [Fact]
    public void A_critical_rocket_wears_the_crit_glow_beside_the_trail()
    {
        // Stock attaches a SECOND simultaneous system (pEffectCrit) beside the
        // trail; ParticleSystemStartExtra is that slot. The flag rides the
        // same wire the trail reads.
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.State.BindLocal();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: true, ownerIndex: 3);

        var client = new TrailRig();
        var mirror = client.Rocket(rocket.State.Snapshot());

        Assert.True(mirror.State.Critical);
        Assert.Equal("critical_rocket_red",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal("rockettrail",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void The_crit_glow_follows_the_team_and_the_default_is_the_eye_boss()
    {
        var blue = new TrailRig();
        blue.Rocket(RocketBlob(team: 3, critical: true));
        Assert.Equal("critical_rocket_blue",
                     blue.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));

        // Stock's default arm names the eye boss whatever the team.
        var unassigned = new TrailRig();
        unassigned.Rocket(RocketBlob(team: 0, critical: true));
        Assert.Equal("eyeboss_projectile",
                     unassigned.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_non_critical_shot_asks_for_no_glow()
    {
        var client = new TrailRig();
        client.Rocket(RocketBlob(team: 2, critical: false));

        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    [Fact]
    public void A_critical_sticky_names_the_grenade_and_a_pipe_the_pipe_glow()
    {
        var sticky = new TrailRig();
        sticky.Sticky(StickyBlob(team: 3, critical: true));
        Assert.Equal("critical_grenade_blue",
                     sticky.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));

        var pipe = new TrailRig();
        pipe.Pipe(PipeBlob(team: 2, critical: true));
        Assert.Equal("critical_pipe_red",
                     pipe.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));

        // Stock's default arm creates no glow at all.
        var stray = new TrailRig();
        stray.Pipe(PipeBlob(team: 0, critical: true));
        Assert.Equal(0, stray.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    [Fact]
    public void A_restart_rearms_the_glow_and_removal_stops_it()
    {
        var client = new TrailRig();
        var mirror = client.Rocket(RocketBlob(team: 2, critical: true));
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));

        // A deflection retargets the team: stock stops pEffectCrit before the
        // re-create, and the new glow is the new team's.
        byte[] second = RocketBlob(team: 3, critical: true);
        mirror.State.Decode(second);
        mirror.OnDataChanged(second, DataUpdateType.DataTableChanged);

        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStopExtra"));
        Assert.Equal("critical_rocket_blue",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));

        mirror.OnRemoved();
        Assert.Equal(2, client.Engine.CountOf("Effects.ParticleSystemStopExtra"));
        Assert.Equal(2, client.Engine.CountOf("Effects.ParticleSystemStop"));
    }

    [Fact]
    public void A_thousand_updates_never_grow_the_crit_glow()
    {
        var client = new TrailRig();
        var mirror = client.Rocket(RocketBlob(team: 3, critical: true));

        for (int i = 0; i < 1000; i++)
            mirror.OnDataChanged(RocketBlob(team: 3, critical: true), DataUpdateType.DataTableChanged);

        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStopExtra"));
    }

    [Fact]
    public void A_deflected_rocket_s_trail_restarts_under_the_new_team()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.State.BindLocal();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 3);

        var client = new TrailRig();
        byte[] first = rocket.State.Snapshot();
        var mirror = client.Rocket(first, arrive: false);
        mirror.OnDataChanged(first, DataUpdateType.Created);
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStart"));

        // The deflector is blue: the retarget is the wire change stock's
        // OnDataChanged sees, and the trail is stopped and re-created.
        rig.Engine.Returns("Players.GetTeam", 3);
        // Stock's Deflected hands the rocket the deflector's active weapon
        // (tf_projectile_rocket.cpp:154); the trail re-reads the item under it.
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_flamethrower");
        rocket.DeflectBy(new EntityHandle(0x0001_0000u | 7), new Vector(0, 0, 1100), deflectorCritBoosted: false);
        byte[] second = rocket.State.Snapshot();
        mirror.State.Decode(second);
        mirror.OnDataChanged(second, DataUpdateType.DataTableChanged);

        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStop"));
        var starts = client.Engine.AllOf("Effects.ParticleSystemStart").ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal("rockettrail", starts[1].ArgAs<string>(1));
    }

    [Fact]
    public void A_deflected_pipe_s_trail_restarts_under_the_deflectors_team()
    {
        // GetTrailParticleName's deflected arm (tf_weapon_grenade_pipebomb.cpp:202-210)
        // names the deflector's team; the deflection is on the wire as the
        // retargeted team the server's Deflected writes, so the mirror's
        // re-create trigger sees it without a deflected field.
        var rig = new ProjectileRig();
        var pipe = rig.Pipebomb();
        pipe.State.BindLocal();

        var client = new TrailRig();
        byte[] first = pipe.State.Snapshot();
        var mirror = client.Pipe(first);
        Assert.Equal("pipebombtrail_red", client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));

        // The deflector is blue.
        rig.Engine.Returns("Players.GetTeam", 3);
        pipe.DeflectedBy(new EntityHandle(0x0001_0000u | 7), deflectorCritBoosted: false, "tf_flamethrower");
        byte[] second = pipe.State.Snapshot();
        mirror.State.Decode(second);
        mirror.OnDataChanged(second, DataUpdateType.DataTableChanged);

        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStop"));
        var starts = client.Engine.AllOf("Effects.ParticleSystemStart").ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal("pipebombtrail_blue", starts[1].ArgAs<string>(1));
    }

    [Fact]
    public void A_thousand_updates_never_grow_the_running_trail()
    {
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.State.BindLocal();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 3);

        var client = new TrailRig();
        var mirror = client.Rocket(rocket.State.Snapshot());

        for (int i = 0; i < 1000; i++)
            mirror.OnDataChanged(rocket.State.Snapshot(), DataUpdateType.DataTableChanged);

        // One system per mirror: the shell's entry cannot accumulate when the
        // mirror re-asks nothing.
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStart"));
        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStop"));

        // And the removal hands the one back.
        mirror.OnRemoved();
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStop"));
    }

    // =====================================================================
    // The rocket's arms.
    // =====================================================================

    [Fact]
    public void A_rocket_over_water_names_the_underwater_trail()
    {
        var client = new TrailRig(water: true);

        client.Rocket(RocketBlob(team: 2));

        Assert.Equal("rockettrail_underwater",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
        // The contents read is of the rocket's own index — the origin stock
        // reads GetAbsOrigin() of.
        Assert.Equal(7, client.Engine.Last("Effects.ClientEntityOrigin")!.Value.ArgAs<int>(0));
    }

    [Fact]
    public void An_unassigned_team_keeps_the_underwater_name()
    {
        var client = new TrailRig();

        client.Rocket(RocketBlob(team: 0));

        Assert.Equal("rockettrail_underwater",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_rocket_without_a_trail_attachment_draws_nothing()
    {
        var client = new TrailRig();
        client.Engine.Returns("EntityRender.LookupAttachment", 0);

        client.Rocket(RocketBlob(team: 2));

        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    // =====================================================================
    // The launcher's arms (CreateTrails :99-118, GetTrailParticleName :150-163).
    // =====================================================================

    /// <summary>Arms the wire the way the server's ArmedBy does, owner and launcher named.</summary>
    private static byte[] FiredBlob(int ownerIndex, string weapon, int team = 2, bool critical = false)
    {
        var state = new TFRocketNetState();
        state.BindLocal();
        state.TeamNumber = team;
        state.Critical = critical;
        state.OwnerIndex = ownerIndex;
        state.WeaponName = weapon;
        return state.Snapshot();
    }

    [Fact]
    public void A_rocket_from_a_launcher_with_no_self_blast_dmg_wears_the_rocket_jumper_trail()
    {
        // GetTrailParticleName (:150-163): the launcher item's
        // no_self_blast_dmg names rockettrail_RocketJumper.
        var client = new TrailRig();
        client.Owners.Holds("no_self_blast_dmg");

        client.Rocket(FiredBlob(3, "tf_weapon_rocketlauncher"));

        Assert.Equal("rockettrail_RocketJumper",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_launcher_without_the_flag_keeps_the_stock_trail()
    {
        // The arm is the attribute's, not the item's mere presence: an item
        // carrying no no_self_blast_dmg falls to rockettrail.
        var client = new TrailRig();
        client.Owners.Holds("unrelated_flag");

        client.Rocket(FiredBlob(3, "tf_weapon_rocketlauncher"));

        Assert.Equal("rockettrail", client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void An_airstrike_rocket_wears_the_airstrike_trail()
    {
        // CreateTrails' mini_rockets arm (:101-107): the launcher item's
        // mini_rockets names rockettrail_airstrike, before the
        // GetTrailParticleName fallthrough is ever reached.
        var client = new TrailRig();
        client.Owners.Holds("mini_rockets");

        client.Rocket(FiredBlob(3, "tf_weapon_rocketlauncher"));

        Assert.Equal("rockettrail_airstrike",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void The_mini_rocket_arm_wins_over_the_rocket_jumper_flag()
    {
        // Stock's order: bUsingCustom is set by the mini rocket arm (:108),
        // so a launcher carrying both never reaches the RocketJumper pick.
        var client = new TrailRig();
        client.Owners.Holds("mini_rockets", "no_self_blast_dmg");

        client.Rocket(FiredBlob(3, "tf_weapon_rocketlauncher"));

        Assert.Equal("rockettrail_airstrike",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_blastjumping_owners_airstrike_rocket_adds_the_line_in_the_extra_slot()
    {
        // :110-115, the condition off the owner's networked words, as stock's
        // m_Shared.InCond reads them. The shell's Extra slot carries it.
        var client = new TrailRig();
        client.Owners.Holds("mini_rockets");
        client.Carriers.Holds(3, client::TF2.Shared.TFCond.Blastjumping);

        client.Rocket(FiredBlob(3, "tf_weapon_rocketlauncher"));

        Assert.Equal("rockettrail_airstrike_line",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void An_airstrike_rocket_whose_owner_is_not_blastjumping_keeps_the_crit_glow()
    {
        // No TF_COND_BLASTJUMPING: no line; the Extra slot stays the crit's,
        // and a grounded owner's carrier is enough to prove the gate.
        var client = new TrailRig();
        client.Owners.Holds("mini_rockets");
        client.Carriers.Holds(3, client::TF2.Shared.TFCond.Blastjumping);
        client.Carriers.Holds(3);

        client.Rocket(FiredBlob(3, "tf_weapon_rocketlauncher", critical: true));

        Assert.Equal("critical_rocket_red",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void The_armed_rocket_writes_its_owner_and_launcher_to_the_wire()
    {
        // The trail arms read the owner's carrier row and the conditions: the
        // owner index and the launcher name ride the same state the team does
        // (stock's networked m_hOwnerEntity and m_hLauncher).
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.State.BindLocal();

        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher_directhit",
                       critical: false, ownerIndex: 3);

        Assert.Equal(3, rocket.State.OwnerIndex);
        Assert.Equal("tf_weapon_rocketlauncher_directhit", rocket.State.WeaponName);
    }

    [Fact]
    public void A_deflection_repoints_the_owner_and_takes_the_deflectors_weapon()
    {
        // Stock's Deflected: SetOwnerEntity(deflector) (:165) and
        // SetLauncher(deflector's active weapon) (:154).
        var rig = new ProjectileRig();
        var rocket = rig.Rocket();
        rocket.State.BindLocal();
        rocket.ArmedBy(90f, TFWeaponId.Rocketlauncher, "tf_weapon_rocketlauncher",
                       critical: false, ownerIndex: 3);
        rig.Engine.Returns("Players.GetActiveWeapon", "tf_flamethrower");
        rig.Engine.Returns("Entities.GetIndex", 9);

        rocket.DeflectBy(new EntityHandle(0x0001_0000u | 9), new Vector(0, 0, 1100), deflectorCritBoosted: false);

        Assert.Equal(9, rocket.State.OwnerIndex);
        Assert.Equal("tf_flamethrower", rocket.State.WeaponName);
    }

    // =====================================================================
    // The pipes' names.
    // =====================================================================

    [Fact]
    public void The_pipebomb_names_red_by_default_and_blue_by_team()
    {
        var red = new TrailRig();
        red.Pipe(PipeBlob(team: 0));
        Assert.Equal("pipebombtrail_red",
                     red.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));

        var blue = new TrailRig();
        blue.Pipe(PipeBlob(team: 3));
        Assert.Equal("pipebombtrail_blue",
                     blue.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    /// <summary>
    /// The wire's team colours the one trail the mirror starts: a red pipe's
    /// mirror starts exactly one system, the red trail, and no second colour
    /// joins it (no crit glow for a plain shot, no blu twin).
    /// </summary>
    [Fact]
    public void A_red_pipe_starts_only_the_red_trail()
    {
        var client = new TrailRig();
        client.Pipe(PipeBlob(team: 2));

        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStart"));
        Assert.Equal("pipebombtrail_red",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    /// <summary>And a blu pipe wears only its own colour — the same single system.</summary>
    [Fact]
    public void A_blu_pipe_starts_only_the_blue_trail()
    {
        var client = new TrailRig();
        client.Pipe(PipeBlob(team: 3));

        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStart"));
        Assert.Equal("pipebombtrail_blue",
                     client.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    [Fact]
    public void The_stickybomb_names_the_sticky_systems()
    {
        var blue = new TrailRig();
        blue.Sticky(StickyBlob(team: 3));
        Assert.Equal("stickybombtrail_blue",
                     blue.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));

        var red = new TrailRig();
        red.Sticky(StickyBlob(team: 2));
        Assert.Equal("stickybombtrail_red",
                     red.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    // =====================================================================
    // The stickybomb's pulse.
    // =====================================================================

    [Fact]
    public void A_pulsed_sticky_wears_the_pulse_in_the_extra_slot()
    {
        // Simulate's team arm (tf_weapon_grenade_pipebomb.cpp:356-363):
        // stickybomb_pulse_red for red, blue for everyone else. The shell
        // carries two systems per mirror, so the pulse wears the Extra slot
        // stock's crit glow holds (the class doc's divergence).
        var red = new TrailRig();
        red.Sticky(StickyBlob(team: 2, pulsed: true));
        Assert.Equal("stickybomb_pulse_red",
                     red.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal("stickybombtrail_red",
                     red.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));

        var blue = new TrailRig();
        blue.Sticky(StickyBlob(team: 3, pulsed: true));
        Assert.Equal("stickybomb_pulse_blue",
                     blue.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void An_unpulsed_critical_sticky_keeps_the_glow_and_never_pulses()
    {
        var client = new TrailRig();
        client.Sticky(StickyBlob(team: 2, critical: true));
        Assert.Equal("critical_grenade_red",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    [Fact]
    public void The_pulse_flips_the_extra_slot_without_restarting_the_trail()
    {
        // Simulate starts the pulse without re-running CreateTrails — the
        // trail keeps running. The crit glow steps out of the slot.
        var client = new TrailRig();
        var mirror = client.Sticky(StickyBlob(team: 2, critical: true));
        Assert.Equal("critical_grenade_red",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStart"));

        byte[] second = StickyBlob(team: 2, critical: true, pulsed: true);
        mirror.State.Decode(second);
        mirror.OnDataChanged(second, DataUpdateType.DataTableChanged);

        Assert.Equal(0, client.Engine.CountOf("Effects.ParticleSystemStop"));
        Assert.Equal("stickybomb_pulse_red",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStart"));
        Assert.Equal(2, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    /// <summary>
    /// Stock's Simulate (<c>tf_weapon_grenade_pipebomb.cpp:356-362</c>): once
    /// the sticky is live the pulse names its own team — red for RED — and the
    /// <c>m_bPulsed</c> gate runs it once; a second live dispatch adds nothing.
    /// </summary>
    [Fact]
    public void A_red_sticky_pulses_red_once()
    {
        var client = new TrailRig();
        var mirror = client.Sticky(StickyBlob(team: 2, pulsed: true));
        Assert.Equal("stickybomb_pulse_red",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));

        // The flag stays true: stock's Simulate creates the pulse once.
        byte[] second = StickyBlob(team: 2, pulsed: true);
        mirror.State.Decode(second);
        mirror.OnDataChanged(second, DataUpdateType.DataTableChanged);
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    /// <summary>The else arm (<c>:360-362</c>): every non-red team pulses blue.</summary>
    [Fact]
    public void A_blu_sticky_pulses_blue()
    {
        var client = new TrailRig();
        client.Sticky(StickyBlob(team: 3, pulsed: true));

        Assert.Equal("stickybomb_pulse_blue",
                     client.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal(1, client.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    // =====================================================================
    // Rigs.
    // =====================================================================

    /// <summary>The server's own netstate, written and snapped: the wire.</summary>
    private static byte[] RocketBlob(int team, bool critical = false)
    {
        var state = new TFRocketNetState();
        state.BindLocal();
        state.TeamNumber = team;
        state.Critical = critical;
        return state.Snapshot();
    }

    private static byte[] PipeBlob(int team, bool critical = false)
    {
        var state = new TFPipebombNetState();
        state.BindLocal();
        state.TeamNumber = team;
        state.Critical = critical;
        return state.Snapshot();
    }

    private static byte[] StickyBlob(int team, bool critical = false, bool pulsed = false)
    {
        var state = new TFStickybombNetState();
        state.BindLocal();
        state.TeamNumber = team;
        state.Critical = critical;
        state.Pulsed = pulsed;
        return state.Snapshot();
    }

    /// <summary>The client doubles: one engine as render and effects, and the splash.</summary>
    private sealed class TrailRig
    {
        public readonly CAbi.RecordingEngine Engine = new();
        public readonly SplashSpy Splash = new();
        public readonly ClientOwners Owners = new();
        public readonly ClientCarriers Carriers = new();

        public TrailRig(bool water = false)
        {
            Engine.Returns("Effects.ParticleSystemStart", true);
            Engine.Returns("Effects.ParticleSystemStartExtra", true);
            Engine.Returns("Effects.ParticleSystemFollowPoints", true);
            // A model with a trail attachment, unless a fact refuses it.
            Engine.Returns("EntityRender.LookupAttachment", 1);
            Engine.Returns("Effects.ClientEntityOrigin", water);
            Engine.Returns("Effects.ClientEntityOrigin.Origin", new Vector(4, 5, 6));
            if (water)
                Splash.Contents = (int)client::SourceSharp.Client.Interop.IParticleMaskTrace.MaskWater;
        }

        public ClientRocket Rocket(byte[] blob, bool arrive = true)
        {
            var mirror = new ClientRocket(Engine, Engine, Splash, Carriers, Owners,
                                          new client::TF2.Shared.Econ.AttributeHooks());
            mirror.State.BindLocal();
            mirror.BindIndex(7);
            mirror.State.Decode(blob);
            if (arrive)
                mirror.OnDataChanged(blob, DataUpdateType.Created);
            return mirror;
        }

        public ClientPipe Pipe(byte[] blob)
        {
            var mirror = new ClientPipe(Engine, Engine);
            mirror.State.BindLocal();
            mirror.State.Decode(blob);
            mirror.OnDataChanged(blob, DataUpdateType.Created);
            return mirror;
        }

        public ClientSticky Sticky(byte[] blob)
        {
            var mirror = new ClientSticky(Engine, Engine);
            mirror.State.BindLocal();
            mirror.State.Decode(blob);
            mirror.OnDataChanged(blob, DataUpdateType.Created);
            return mirror;
        }
    }

    /// <summary>The client's owners: the item the facts build, for every ask.</summary>
    private sealed class ClientOwners : client::TF2.Shared.ITFWeaponOwners
    {
        private client::TF2.Shared.Econ.IAttributeProvider? _item;

        /// <summary>Hands out one launcher item carrying each named int attribute.</summary>
        public void Holds(params string[] hooks)
        {
            var container = new client::TF2.Shared.Econ.AttributeContainer(
                null, client::TF2.Shared.Econ.AttributeProviderType.Weapon);
            var definition = 90;
            foreach (var hook in hooks)
                container.Set(client::TF2.Shared.Econ.AttributeValue.OfFloat(
                    new client::TF2.Shared.Econ.AttributeDefinition(definition++, "attr " + hook, hook,
                        client::TF2.Shared.Econ.AttributeDescriptionFormat.Additive,
                        client::TF2.Shared.Econ.AttributeEffectType.Neutral, false, false,
                        client::TF2.Shared.Econ.AttributeValueType.Default,
                        client::TF2.Shared.Econ.MergedKeys.Block("x")), 1f));
            _item = container;
        }

        public client::TF2.Shared.TFClass ClassOf(int playerIndex) => client::TF2.Shared.TFClass.Undefined;
        public client::TF2.Shared.Conditions.TFConditions? ConditionsOf(int playerIndex) => null;
        public float CritMult(int playerIndex) => 1f;
        public bool WinsCrits(int playerIndex) => false;
        public client::TF2.Shared.Econ.IAttributeProvider? ItemOf(int playerIndex, string weaponName) => _item;
        public client::TF2.Shared.Econ.IAttributeProvider? ManagerOf(int playerIndex) => null;
        public bool OffGround(int playerIndex) => false;
        public bool EyesUnderwater(int playerIndex) => false;
        public bool DuckingOf(int playerIndex) => false;
    }

    /// <summary>The client's decoded carriers, the facts hand out by entity index.</summary>
    private sealed class ClientCarriers : client::TF2.Shared.Player.ITFPlayerData
    {
        public readonly Dictionary<int, client::TF2.Shared.Player.TFPlayerNetState> States = new();

        /// <summary>Hands out a carrier whose condition words name <paramref name="conds"/>.</summary>
        public void Holds(int entityIndex, params client::TF2.Shared.TFCond[] conds)
        {
            var state = new client::TF2.Shared.Player.TFPlayerNetState();
            state.BindLocal();
            client::TF2.Shared.Player.TFPlayerNetStateViews.SetConditions(
                state, client::TF2.Shared.Conditions.TFCondMask.Of(conds));
            States[entityIndex] = state;
        }

        public client::TF2.Shared.Player.TFPlayerNetState? Predicted(int playerIndex)
            => States.GetValueOrDefault(playerIndex);

        public client::TF2.Shared.Player.TFPlayerNetState? Networked(int playerIndex)
            => States.GetValueOrDefault(playerIndex);
    }

    /// <summary>The splash carry-out: one contents answer.</summary>
    private sealed class SplashSpy : CAbi.IClientSplashCarryOut
    {
        public int Contents;

        public int PointContents(Vector point) => Contents;

        public Vector Velocity(nint physicsObject) => throw new NotSupportedException();
        public Vector CollideExtent(nint physicsObject, Vector origin, Vector angles, Vector direction)
            => throw new NotSupportedException();
        public (Vector Color, float Luminosity) SplashLighting(Vector point) => throw new NotSupportedException();
        public void SplashEffect(int kind, Vector origin, Vector normal, float scale, Vector color,
                                 float lifetime, float alpha) { }
    }
}
