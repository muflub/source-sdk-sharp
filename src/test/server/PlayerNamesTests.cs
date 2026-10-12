using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A rename fires stock's player_changename (CTeamplayRules::ClientSettingsChanged):
/// userid, oldname, newname, only when the name really changed and the player
/// had one.
/// </summary>
public class PlayerNamesTests
{
    private sealed class Events : IEngineGameEvents
    {
        private readonly Dictionary<IntPtr, (string Name, Dictionary<string, object> Fields)> _live = new();
        private int _next = 1;
        public readonly List<(string Name, Dictionary<string, object> Fields)> Fired = new();

        public IntPtr EventCreate(string name)
        {
            var h = new IntPtr(_next++);
            _live[h] = (name, new Dictionary<string, object>());
            return h;
        }
        public void EventSetInt(IntPtr h, string key, int value) => _live[h].Fields[key] = value;
        public void EventSetFloat(IntPtr h, string key, float value) => _live[h].Fields[key] = value;
        public void EventSetBool(IntPtr h, string key, bool value) => _live[h].Fields[key] = value;
        public void EventSetString(IntPtr h, string key, string value) => _live[h].Fields[key] = value;
        public bool EventFire(IntPtr h)
        {
            Fired.Add(_live[h]);
            _live.Remove(h);
            return true;
        }
        public void EventFree(IntPtr h) => _live.Remove(h);
        public int EventGetInt(IntPtr h, string key) => 0;
        public float EventGetFloat(IntPtr h, string key) => 0f;
        public bool EventGetBool(IntPtr h, string key) => false;
        public string EventGetString(IntPtr h, string key) => "";
        public void ListenForGameEvent(string name) { }
    }

    private sealed class Clients : IConnectedClients
    {
        public string Name = "";
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => name == "name" ? Name : null;
        public int UserId(int entIndex) => entIndex + 10;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private static (PlayerNames Names, Events Events, Clients Clients) Rig()
    {
        var events = new Events();
        var clients = new Clients();
        return (new PlayerNames(events, clients), events, clients);
    }

    [Fact]
    public void A_rename_fires_player_changename_with_stocks_fields_and_takes_the_name()
    {
        var (names, events, clients) = Rig();
        names.PutInServer(2, "alice");
        clients.Name = "bob";

        Assert.True(names.Changed(2), "the native player takes the new name");

        var (name, fields) = Assert.Single(events.Fired);
        Assert.Equal("player_changename", name);
        Assert.Equal(12, fields["userid"]);
        Assert.Equal("alice", fields["oldname"]);
        Assert.Equal("bob", fields["newname"]);
        Assert.Equal("bob", names.NameOf(2));
    }

    [Fact]
    public void Settings_that_leave_the_name_alone_fire_nothing()
    {
        var (names, events, clients) = Rig();
        names.PutInServer(2, "alice");
        clients.Name = "alice";

        Assert.False(names.Changed(2));
        Assert.Empty(events.Fired);
    }

    [Fact]
    public void The_compare_is_case_sensitive()
    {
        var (names, events, clients) = Rig();
        names.PutInServer(2, "alice");
        clients.Name = "Alice";

        Assert.True(names.Changed(2));
        Assert.Single(events.Fired);
    }

    [Fact]
    public void A_player_with_no_name_yet_takes_none_and_fires_nothing()
    {
        var (names, events, clients) = Rig();
        clients.Name = "bob";

        Assert.False(names.Changed(3));
        Assert.Empty(events.Fired);
    }

    [Fact]
    public void A_second_rename_reports_the_first_new_name_as_old()
    {
        var (names, events, clients) = Rig();
        names.PutInServer(2, "alice");
        clients.Name = "bob";
        names.Changed(2);
        clients.Name = "carol";
        names.Changed(2);

        Assert.Equal(("bob", "carol"), ((string)events.Fired[1].Fields["oldname"], (string)events.Fired[1].Fields["newname"]));
    }
}
