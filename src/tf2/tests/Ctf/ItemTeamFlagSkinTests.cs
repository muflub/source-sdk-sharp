using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Entities.Ctf;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The flag's skin on the seam that reaches the client: <c>m_nSkin</c>
/// (entity_capture_flag.cpp's <c>Activate</c> and <c>SetFlagSkin</c>) is
/// written through <see cref="IServerSkins"/>, the block write that sends
/// m_nSkin, so the client's <c>GetSkin</c> answers it and the briefcase is
/// drawn in its team's skin. On the server's own <c>SetSkin</c> the arm is
/// empty, so there the choice reached no client and every flag drew in the
/// red's skin (the orange-red BLU briefcase). The arithmetic: the team's own
/// number less two (Unassigned is 2), and <see cref="ItemTeamFlag.NumberOfSkins"/>
/// brighter while carried.
/// </summary>
public sealed class ItemTeamFlagSkinTests
{
    /// <summary>The skins the rig's server seam recorded, as the registration holds it.</summary>
    private static RecordingServerSkins Skins(CtfRig rig)
        => (RecordingServerSkins)rig.Registry.Services!.Resolve(typeof(IServerSkins))!;

    /// <summary>The last skin the seam was asked for on this entity.</summary>
    private static int LastSkin(RecordingServerSkins skins, EntityHandle entity)
        => skins.Set.Last(c => c.Entity == entity).Skin;

    [Fact]
    public void A_spawned_blu_flag_records_skin_1_through_the_server_seam()
    {
        var rig = new CtfRig();
        var blue = rig.Flag(TFTeam.Blue);

        Assert.Equal(1, LastSkin(Skins(rig), blue.Handle));
    }

    [Fact]
    public void A_spawned_red_flag_records_skin_0_through_the_server_seam()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);

        Assert.Equal(0, LastSkin(Skins(rig), red.Handle));
    }

    [Fact]
    public void A_spawned_unassigned_flag_records_skin_2_through_the_server_seam()
    {
        var rig = new CtfRig();
        var flag = rig.Flag(TFTeam.Unassigned);

        Assert.Equal(2, LastSkin(Skins(rig), flag.Handle));
    }

    [Fact]
    public void Picking_a_flag_up_records_its_skin_NumberOfSkins_brighter_through_the_seam()
    {
        var rig = new CtfRig();
        var blue = rig.Flag(TFTeam.Blue);
        var redPlayer = rig.Players.Add(2, TFTeam.Red);

        rig.Registry.Touch(blue.Handle, redPlayer);

        Assert.Equal(1 + ItemTeamFlag.NumberOfSkins, LastSkin(Skins(rig), blue.Handle));
    }

    [Fact]
    public void Dropping_a_flag_records_its_team_skin_back_through_the_seam()
    {
        var rig = new CtfRig();
        var blue = rig.Flag(TFTeam.Blue);
        var redPlayer = rig.Players.Add(2, TFTeam.Red);

        rig.Registry.Touch(blue.Handle, redPlayer);
        blue.Drop(redPlayer, visible: true);

        Assert.Equal(1, LastSkin(Skins(rig), blue.Handle));
    }
}
