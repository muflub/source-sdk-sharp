extern alias client;

using Xunit;

using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using CAbi = client::SourceSharp.Abi;
using CShared = client::SourceSharp.Shared;
using CTF = client::TF2.Shared;
using TFClientPlayer = client::TF2.Client.Player;
using TFClientWeapons = client::TF2.Client.Weapons;
using TFTeam = client::TF2.Shared.TFTeam;

using TF2.Tests.Weapons;

namespace TF2.Tests.Client;

/// <summary>
/// The client's half of <c>CTFViewModel::GetSkin</c>
/// (tf_viewmodel.cpp:362-398): the mirror's predicted deploy asks it, so the
/// replayed switch writes the byte the server's
/// <c>TFViewmodelTeamSkinRule</c> wrote instead of mispredicting it for a
/// round trip. The team is the player resource's column; the decision is the
/// shared <c>TFViewmodelSkins</c>, whose arms the server rule's facts cover.
/// </summary>
public sealed class TFClientViewmodelTeamSkinTests
{
    private const int Player = 2;

    private sealed class ClientFiles(TFClassTableTests.Files files) : CAbi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);

        public Dictionary<string, byte[]> Bytes => files.Bytes;
    }

    private static CTF.TFWeaponTypes Types(string? schemaText = null)
    {
        var files = new ClientFiles(WeaponFixtures.ShippedWithClasses());
        if (schemaText is not null)
            files.Bytes[CTF.Econ.ItemSchema.FilePath] = System.Text.Encoding.UTF8.GetBytes(schemaText);
        var log = new ClientLog();
        return new CTF.TFWeaponTypes(new CTF.TFWeaponScripts(log, files),
                                     new CTF.TFClassTable(log, files),
                                     new CTF.Econ.ItemSchema(files, log));
    }

    private static TFClientWeapons.TFClientViewmodelTeamSkinRule Rule(CTF.TFWeaponTypes types,
                                                                      TFClientPlayer.TFClientPlayerResources resources)
        => new(types, new CTF.TFWeaponScripts(new ClientLog(), new ClientFiles(WeaponFixtures.ShippedWithClasses())), resources);

    private static CShared.WeaponType Row(CTF.TFWeaponTypes types, string name)
        => types.Rows().First(r => r.Name == name);

    private static (TFClientWeapons.TFClientViewmodelTeamSkinRule Rule, TFClientPlayer.TFClientPlayerResources Resources)
        Rig(TFTeam team, CTF.TFWeaponTypes? types = null)
    {
        var resources = new TFClientPlayer.TFClientPlayerResources();
        var resource = new TFClientPlayer.ClientTFPlayerResource(new ClientEngine(), resources);
        resource.State.BindLocal();
        resource.State.Team[Player] = (byte)team;
        resources.Live = resource;
        return (Rule(types ?? Types(), resources), resources);
    }

    [Fact]
    public void The_blu_medic_s_medigun_wears_skin_1()
    {
        var types = Types();
        var (rule, _) = Rig(TFTeam.Blue, types);

        Assert.Equal(1, rule.Skin(Player, Row(types, "tf_weapon_medigun"), null));
    }

    [Fact]
    public void A_resource_not_yet_arrived_leaves_the_answer()
    {
        var resources = new TFClientPlayer.TFClientPlayerResources();
        var rule = Rule(Types(), resources);

        Assert.Equal(7, rule.Skin(Player, Row(Types(), "tf_weapon_medigun"), 7));
    }
}
