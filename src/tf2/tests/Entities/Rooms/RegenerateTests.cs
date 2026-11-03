using SourceSharp.Abi;
using TF2.Server.Entities.Rooms;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Entities.Rooms;

/// <summary>
/// func_regenerate (stage 2.4): <c>CRegenerateZone::Touch</c> and
/// <c>Regenerate</c> (func_regenerate.cpp): a full resupply for the zone's
/// team, then TF_REGENERATE_NEXT_USE_TIME (3 s) before the next.
/// </summary>
public sealed class RegenerateTests
{
    private static (MapItemRig Rig, FuncRegenerate Locker) Locker(TFTeam team = TFTeam.Red, string model = "")
    {
        var rig = new MapItemRig();
        var keys = new List<(string, string)> { ("TeamNum", ((int)team).ToString()) };
        if (model.Length > 0)
            keys.Add(("associatedmodel", model));
        var locker = rig.Spawn<FuncRegenerate>(FuncRegenerate.MapClass, keys.ToArray());
        return (rig, locker);
    }

    [Fact]
    public void A_player_of_the_lockers_team_is_resupplied_once_per_three_seconds()
    {
        var (rig, locker) = Locker();
        var (player, p) = rig.Players.Add(TFTeam.Red);

        rig.Clock.Now = 10f;
        rig.Touch(locker, player);
        Assert.Equal(1, p.Regenerated);
        Assert.Contains("sound Regenerate.Touch", rig.Notices.Said);

        // Every frame inside touches it; the cooldown holds until 13 s.
        rig.Clock.Now = 12.9f;
        rig.Touch(locker, player);
        Assert.Equal(1, p.Regenerated);

        rig.Clock.Now = 13.01f;
        rig.Touch(locker, player);
        Assert.Equal(2, p.Regenerated);
    }

    [Fact]
    public void A_locker_hears_every_frame_so_a_player_waiting_inside_is_resupplied_when_the_cooldown_ends()
    {
        var (_, locker) = Locker();

        // Stock's Touch (not StartTouch) runs each frame of the overlap; the
        // frame diff asks the class (ScriptedEntity.TouchEveryTick).
        Assert.True(locker.TouchEveryTick);
    }

    [Fact]
    public void The_cooldown_is_the_players_across_lockers()
    {
        var (rig, first) = Locker();
        var second = rig.Spawn<FuncRegenerate>(FuncRegenerate.MapClass, ("TeamNum", "2"));
        var (player, p) = rig.Players.Add(TFTeam.Red);

        rig.Clock.Now = 5f;
        rig.Touch(first, player);
        rig.Clock.Now = 6f;
        rig.Touch(second, player);

        Assert.Equal(1, p.Regenerated);
    }

    [Fact]
    public void The_other_team_is_refused_and_a_teamless_locker_serves_both()
    {
        var (rig, red) = Locker(TFTeam.Red);
        var anyone = rig.Spawn<FuncRegenerate>(FuncRegenerate.MapClass);
        var (blue, b) = rig.Players.Add(TFTeam.Blue);

        rig.Touch(red, blue);
        Assert.Equal(0, b.Regenerated);

        rig.Touch(anyone, blue);
        Assert.Equal(1, b.Regenerated);
    }

    [Fact]
    public void After_a_win_every_locker_serves_the_winners_and_none_the_losers()
    {
        var (rig, red) = Locker(TFTeam.Red);
        var (redPlayer, r) = rig.Players.Add(TFTeam.Red);
        var (bluePlayer, b) = rig.Players.Add(TFTeam.Blue);
        rig.Round.State = RoundState.TeamWin;
        rig.Round.WinningTeam = TFTeam.Blue;

        rig.Touch(red, redPlayer);
        rig.Touch(red, bluePlayer);

        Assert.Equal(0, r.Regenerated);
        Assert.Equal(1, b.Regenerated);
    }

    [Fact]
    public void A_stalemate_or_a_taunt_resupplies_nobody()
    {
        var (rig, locker) = Locker();
        var (player, p) = rig.Players.Add(TFTeam.Red);

        p.Conditions.Add(TFCond.Taunting);
        rig.Touch(locker, player);
        Assert.Equal(0, p.Regenerated);

        p.Conditions.Clear();
        rig.Round.State = RoundState.Stalemate;
        rig.Touch(locker, player);
        Assert.Equal(0, p.Regenerated);

        rig.Round.State = RoundState.RoundRunning;
        rig.Touch(locker, player);
        Assert.Equal(1, p.Regenerated);
    }

    [Fact]
    public void A_disabled_locker_resupplies_nobody()
    {
        var (rig, locker) = Locker();
        var (player, p) = rig.Players.Add(TFTeam.Red);

        rig.Input(locker, "Disable");
        rig.Touch(locker, player);

        Assert.Equal(0, p.Regenerated);
    }

    [Fact]
    public void The_associated_prop_opens_and_closes_two_seconds_later()
    {
        var (rig, locker) = Locker(model: "locker_prop");
        var prop = new EntityHandle(0x0050_0001u);
        rig.Engine.Returns("Entities.FindByName", prop)
                  .Returns("Entities.IsValid", true)
                  .Returns("Entities.FireInput", true);
        locker.OnServerActivate();
        Assert.Equal(prop, locker.AssociatedModel);
        var (player, _) = rig.Players.Add(TFTeam.Red);

        rig.Touch(locker, player);

        var open = rig.Engine.Last("Entities.FireInput")!.Value;
        Assert.Equal((prop, "SetAnimation", "open"), ((EntityHandle)open.Arg(0)!, (string)open.Arg(1)!, (string)open.Arg(2)!));
        Assert.Equal(2f, locker.NextThinkDelay("RegenerateClose"));

        locker.OnThink("RegenerateClose");
        var close = rig.Engine.Last("Entities.FireInput")!.Value;
        Assert.Equal("close", close.Arg(2));
    }
}
