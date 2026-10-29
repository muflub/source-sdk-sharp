using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The local player as the weapon selection reads and moves them: the
/// networked selection bits, the weapon in hand by type, the frozen flag,
/// and a choice sent as the server Arsenal's <c>ss_use</c>.
/// </summary>
public class LocalWeaponSelectionTests
{
    private sealed class Arsenal(string name) : ILocalArsenal
    {
        public bool TryGetActive(out string n, out int c, out int r)
        {
            (n, c, r) = (name, 0, 0);
            return name.Length > 0;
        }
    }

    private sealed class OnePlayer(ClientStandaloneEntity? player) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => player is null ? [] : [player];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == 1 ? player : null;
    }

    private sealed class Sent : IWeaponSelectionCommand, SourceSharp.Client.Sounds.IClientEntitySounds
    {
        public readonly List<string> Lines = new();
        public readonly List<(EntityHandle, string)> Sounds = new();

        public void SendSelection(string line) => Lines.Add(line);
        public void Escape() { }
        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null) => Sounds.Add((entity, soundName));
        public void StopFrom(EntityHandle entity, string soundName) { }
    }

    private static (LocalWeaponSelection Local, ClientSsPlayer Player, Sent Sent) Make(string inHand = "")
    {
        var engine = Fake().Returns("Hud.LocalPlayerIndex", 1);
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0001u) };
        player.State.BindLocal();
        player.State.Alive = true;
        var sent = new Sent();
        return (new LocalWeaponSelection(engine, new OnePlayer(player), new Arsenal(inHand), WeaponCatalog.Stock(),
                                         sent, sent), player, sent);
    }

    [Fact]
    public void The_bits_are_the_local_players_networked_words()
    {
        var (local, player, _) = Make();
        player.State.CarriedWeapons = 0b1011;
        player.State.EmptyWeapons = 0b0010;

        Assert.Equal((0b1011u, 0b0010u), (local.Carried, local.Empty));
    }

    [Fact]
    public void The_weapon_in_hand_is_its_type()
    {
        var (local, _, _) = Make(EntityNames.WeaponSmg1);

        Assert.Equal(WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSmg1), local.Active);
        Assert.Equal(-1, Make().Local.Active);
    }

    [Fact]
    public void A_frozen_player_cannot_select_and_a_dead_one_has_no_hud()
    {
        var (local, player, _) = Make();
        Assert.False(local.FrozenOrDead);

        player.State.Flags |= GameMovement.FlFrozen;
        Assert.True(local.FrozenOrDead);
        Assert.True(local.HudAllowed);

        player.State.Flags = 0;
        player.State.Alive = false;
        Assert.True(local.FrozenOrDead);
        Assert.False(local.HudAllowed);
    }

    [Fact]
    public void A_choice_is_the_arsenals_ss_use_by_classname()
    {
        var (local, _, sent) = Make();

        local.Select(WeaponCatalog.Stock().IndexOf(EntityNames.Weapon357));
        local.LastWeapon();

        Assert.Equal(["ss_use " + EntityNames.Weapon357, "lastinv"], sent.Lines);
    }

    [Fact]
    public void The_sounds_come_from_the_local_player()
    {
        var (local, player, sent) = Make();

        local.Sound("Player.WeaponSelected");

        Assert.Equal([(player.Handle, "Player.WeaponSelected")], sent.Sounds);
    }

    [Fact]
    public void Slots_and_positions_are_the_scripts()
    {
        var scripts = new WeaponScriptDatabase(new RecordingLog(), path => path.EndsWith("weapon_357.txt", StringComparison.Ordinal)
            ? "WeaponData { \"bucket\" \"1\" \"bucket_position\" \"2\" }" : null);
        var slots = new ScriptWeaponSlots(WeaponCatalog.Stock(), scripts);
        int magnum = WeaponCatalog.Stock().IndexOf(EntityNames.Weapon357);

        Assert.Equal((1, 2), (slots.Slot(magnum), slots.Position(magnum)));
        Assert.Equal(-1, slots.Slot(WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol)));
    }
}
