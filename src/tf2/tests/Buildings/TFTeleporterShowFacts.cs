using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Server;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The show: what each end of the pair displays and sounds as the trip goes.
/// A record's particle rides its HitBox index; the rig's per-name table reads
/// the name back off it.
/// </summary>
public class TFTeleporterShowFacts
{
    private static (BuildingsRig Rig, TFTeleporter Entrance, TFTeleporter Exit, EntityHandle Builder) APair(TFTeam team = TFTeam.Red)
        => TeleporterHarness.Pair(team);

    private static void TickBoth(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit, float seconds)
        => TeleporterHarness.Tick(rig, entrance, exit, seconds);

    private static EntityHandle ARider(BuildingsRig rig, TFTeam team = TFTeam.Red)
        => TeleporterHarness.AStandingRider(rig, team);

    private static (TeEffectData Data, TeFilter Filter) Record(BuildingsRig rig, string name)
    {
        (string _, TeEffectData data, TeFilter filter, int _) = rig.Effects.Dispatched
            .Single(effect => effect.Data.HitBox == rig.ParticleIndices[name]);
        return (data, filter);
    }

    private static string NameOf(BuildingsRig rig, TeEffectData data)
        => rig.ParticleIndices.First(kv => kv.Value == data.HitBox).Key;

    private static (EntityHandle Player, EntityHandle Requester, int Fov, float Rate, int Start)[] Fovs(BuildingsRig rig)
        => rig.Engine.AllOf("Players.SetFov")
            .Select(call => ((EntityHandle)call.Args[0]!, (EntityHandle)call.Args[1]!, (int)call.Args[2]!,
                             (float)call.Args[3]!, (int)call.Args[4]!))
            .ToArray();

    [Fact]
    public void TheSendMarksThePadWithTheTeamsPortal()
    {
        var (rig, entrance, exit, _) = APair();
        Vector at = new(64f, 128f, 0f);
        rig.Origins[entrance.Handle] = at;
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.2f);

        (TeEffectData portal, TeFilter filter) = Record(rig, "teleported_red");
        Assert.Equal(at, portal.Origin);

        // CPVSFilter at the origin (tf_obj_teleporter.cpp:99).
        Assert.Equal(TeFilterKind.Pvs, filter.Kind);
        Assert.Equal(at, filter.Origin);
    }

    [Fact]
    public void TheSendRidesTheSparklesOnTheTraveller()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);
        rig.Engine.ReturnsEachCall("Entities.GetIndex", () =>
        {
            EntityHandle who = (rig.Engine.Last("Entities.GetIndex") ?? default).ArgAs<EntityHandle>(0);
            return who == rider ? 4711 : 0;
        });

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.2f);

        TeEffectData sparkles = Record(rig, "player_sparkles_red").Data;

        // The record names the rider and hangs off its origin
        // (PATTACH_ABSORIGIN, tf_obj_teleporter.cpp:114): the entity index,
        // PARTICLE_DISPATCH_FROM_ENTITY, and the attach type where the
        // client's dispatch reads it.
        Assert.Equal(4711, sparkles.EntIndex);
        Assert.Equal(1 << 0, sparkles.Flags & 1 << 0);   // PARTICLE_DISPATCH_FROM_ENTITY
        Assert.Equal(0, sparkles.DamageType);            // PATTACH_ABSORIGIN
    }

    [Fact]
    public void TheArrivalIsThePortalsAlone()
    {
        var (rig, entrance, exit, _) = APair();
        Vector at = new(-256f, 96f, 0f);
        rig.Origins[exit.Handle] = at;
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.2f);

        // The trip is three records — the pad's pair, then the arrival alone
        // at the exit (tf_obj_teleporter.cpp:110-122, :188-198).
        Assert.Equal(new[] { "teleported_red", "player_sparkles_red", "teleportedin_red" },
                     rig.Effects.Dispatched.Select(effect => NameOf(rig, effect.Data)).ToArray());

        (TeEffectData arrival, _) = Record(rig, "teleportedin_red");
        Assert.Equal(at, arrival.Origin);
        Assert.Equal(0, arrival.Flags & 1 << 0);         // no entity rides it
    }

    [Fact]
    public void TheBluePairWearsBlue()
    {
        var (rig, entrance, exit, _) = APair(TFTeam.Blue);
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig, TFTeam.Blue);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.2f);

        Assert.Equal(new[] { "teleported_blue", "player_sparkles_blue", "teleportedin_blue" },
                     rig.Effects.Dispatched.Select(effect => NameOf(rig, effect.Data)).ToArray());
    }

    [Fact]
    public void TheEntranceSoundsItsSend()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.2f);

        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
                        call => (EntityHandle)call.Args[0]! == entrance.Handle
                                && (string)call.Args[1]! == TFTeleporter.SendSound);
    }

    [Fact]
    public void TheExitSoundsItsReceive()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.2f);

        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"),
                        call => (EntityHandle)call.Args[0]! == exit.Handle
                                && (string)call.Args[1]! == TFTeleporter.ReceiveSound);
    }

    [Fact]
    public void TheArrivalFadesTheTravellersScreen()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);

        // Nothing until the arrival lands (the send and the exit's wait are
        // unfaded): the 0.1 s send and the 0.25 s fade-out pass first.
        TickBoth(rig, entrance, exit, 0.3f);
        Assert.Empty(rig.Screen.Fades);

        TickBoth(rig, entrance, exit, 0.1f);

        // RecieveTeleportingPlayer's flash (tf_obj_teleporter.cpp:1024-1025):
        // white at 100, 0.25 s fade and 0.4 s hold, FFADE_IN, on the
        // traveller alone.
        (EntityHandle player, int r, int g, int b, int a, float duration, float hold, FadeFlags flags)
            = Assert.Single(rig.Screen.Fades);
        Assert.Equal(rider, player);
        Assert.Equal((255, 255, 255, 100), (r, g, b, a));
        Assert.Equal((0.25f, 0.4f), (duration, hold));
        Assert.Equal(FadeFlags.In, flags);
    }

    [Fact]
    public void TheArrivalUnzoomsAZoomedSniper()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);
        rig.HealingConditions.Add(rider, TFCond.Zoomed);
        rig.HealingConditions.Add(rider, TFCond.Aiming);

        entrance.OnTouch(rider);

        // The scopes are the sniper's until the arrival (the trip's 0.4 s run
        // first): the conds stand and no word has reached the view.
        TickBoth(rig, entrance, exit, 0.3f);
        Assert.True(rig.HealingConditions.Holds(rider.EntryIndex, TFCond.Zoomed));
        Assert.Empty(Fovs(rig));

        TickBoth(rig, entrance, exit, 0.1f);

        // InstantlySniperUnzoom (tf_player_shared.cpp:1387-1406): the conds
        // go, and the slam (:1402, a zero rate, the rider his own requester)
        // precedes the ramp (:1022) at the stock words.
        Assert.False(rig.HealingConditions.Holds(rider.EntryIndex, TFCond.Zoomed));
        Assert.False(rig.HealingConditions.Holds(rider.EntryIndex, TFCond.Aiming));
        Assert.Equal(new[] { (0, 0f, 0), (0, 0.5f, 120) },
                     Fovs(rig).Select(f => (f.Fov, f.Rate, f.Start)).ToArray());
        Assert.All(Fovs(rig), f =>
        {
            Assert.Equal(rider, f.Player);
            Assert.Equal(rider, f.Requester);
        });
    }

    [Fact]
    public void TheArrivalEasesTheViewBackOverTheConvarsWords()
    {
        var (rig, entrance, exit, _) = APair();
        rig.Convars.Get<TFTeleporterFovTimeConVar>()!.SetValue(1.5f);
        rig.Convars.Get<TFTeleporterFovStartConVar>()!.SetValue(100);
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);
        TickBoth(rig, entrance, exit, 0.4f);

        // One ramp and no slam — the rider arrives unzoomed — at the
        // convars' word (tf_obj_teleporter.cpp:1022): the read is the
        // arrival's, not a literal, and the foot is the caller's iZoomStart.
        (EntityHandle player, EntityHandle requester, int fov, float rate, int start) = Assert.Single(Fovs(rig));
        Assert.Equal(rider, player);
        Assert.Equal(rider, requester);
        Assert.Equal((0, 1.5f, 100), (fov, rate, start));
    }

    [Fact]
    public void TheReleasedTravellerGlowsTeleportedForTwelveSeconds()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 0.05f);
        EntityHandle rider = ARider(rig);

        entrance.OnTouch(rider);

        // The glow is the release's, lit as the trip ends
        // (TeleportEffect, tf_player.cpp:15554-15566, called at
        // tf_obj_teleporter.cpp:1139).
        TickBoth(rig, entrance, exit, 0.3f);
        Assert.DoesNotContain(rig.HealingConditions.Calls, call => call.Contains($" {TFCond.Teleported} "));

        // The release's end: the fade-in's 0.25 s on top of send and
        // fade-out (0.05 + 0.1 + 0.25 + 0.25).
        TickBoth(rig, entrance, exit, 0.4f);

        // The MvM bot arm's 30 s is not here: the stock 12 s on the player
        // (tf_player.cpp:15558).
        Assert.Contains(rig.HealingConditions.Calls,
                        call => call.StartsWith($"add {rider.EntryIndex} {TFCond.Teleported} d12"));
        Assert.True(rig.HealingConditions.Holds(rider.EntryIndex, TFCond.Teleported));
    }

    [Fact]
    public void AnUnsentPadSaysNothing()
    {
        var (rig, entrance, exit, _) = APair();
        int sounded = rig.Engine.CountOf("Sound.EntityEmitSound");
        TickBoth(rig, entrance, exit, 1.0f);

        // The show is the trip's, not the ready state's: a pair idling with
        // nobody booked neither speaks nor sparks (the state sounds are
        // another arm). The count is the idle window's own — the build's
        // line, if the staged rules answer it, stands before it.
        Assert.Empty(rig.Effects.Dispatched);
        Assert.Equal(sounded, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }
}
