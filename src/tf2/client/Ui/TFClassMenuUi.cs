//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The TF2 class menu in game (stock's <c>CTFClassMenu</c>,
/// game/client/tf/vgui/tf_classmenu.cpp): the per-class counts read
/// here off the two player resources — the core's rows for the team and
/// the connected flag, TF's <c>m_iPlayerClass</c> rows beside them —
/// the local player excluded from their own class's count as stock's
/// <c>UpdateNumClassLabels</c> does (:1492-1498), and the class limit
/// off the replicated <c>mp_highlander</c> and <c>tf_classlimit</c>
/// (<c>CTFGameRules::GetClassLimit</c>'s reads, and what
/// <c>CanPlayerChooseClass</c> counts against, tf_gamerules.cpp:17651-17707).
///
/// <para>
/// A choice is stock's <c>Go</c> and <c>Join_Class</c> (:1216-1275,
/// :1330-1339): a class at its limit sends nothing and leaves the menu
/// open; anything else sends the named <c>joinclass</c> — the nine by
/// their raw script names, the random button as <c>joinclass random</c>
/// which the server's own random arm answers — and closes the menu.
/// </para>
///
/// <para>
/// The visibility is told to the server as stock's <c>SetVisible</c> does
/// (:1174-1211): a show of any of the panel names sends <c>menuopen</c>
/// (:1182) and the host's close of the screen — Escape, a named close,
/// the window's own button, the choice's own hide — sends
/// <c>menuclosed</c> once (:1199), the server's
/// <c>m_bIsClassMenuOpen</c>, the respawn wave's hold
/// (<see cref="TF2.Server.Player.TFClassMenuCommands"/>).
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFClassMenuSource), typeof(ITFClassMenuActions)])]
public sealed class TFClassMenuUi(IClientStandalones standalones, IEngineHud screen,
                                  IFwdEngineClient engine, TFClassLimitConVar classLimit,
                                  MpHighlanderConVar highlander, ITFMenuNavigation nav,
                                  ITFLoadoutActions loadout)
    : ITFClassMenuSource, ITFClassMenuActions
{
    private const int Slots = PlayerResourceNetState.Slots;

    private ClientPlayerResource? _core;
    private ClientTFPlayerResource? _tf;

    public int Team
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            return Core is { } r && local is > 0 and < Slots ? r.State.Team[local] : 0;
        }
    }

    public TFClassChoice Choice(int classNumber)
    {
        int players = Count(classNumber);
        int limit = Limit;
        return new TFClassChoice(classNumber, TFClassNames.NonLocalized((TFClass)classNumber), players,
                                 classNumber == SpawnedClass, limit > 0 && players >= limit);
    }

    /// <summary>The class at spawn, <c>m_iPlayerClass</c>: stock gates
    /// Cancel on the DESIRED class, which is not mirrored to the client
    /// (the docs/divergences.md row names it).</summary>
    public bool HasClass => SpawnedClass > 0;

    public void Choose(int classNumber)
    {
        // Stock's Go() (:1216-1275): the undefined arm and the
        // CanPlayerChooseClass arm send nothing and leave the menu open.
        if (classNumber is < 1 or > 9)
            return;
        int limit = Limit;
        if (limit > 0 && Count(classNumber) >= limit)
            return;
        engine.SubmitConsoleLine($"joinclass {TFClassNames.Raw((TFClass)classNumber)}");
        nav.Close("tf2classmenu");
    }

    public void ChooseRandom()
    {
        // The server's random arm picks the class
        // (TFJoinClassCommand.IsRandom); the menu only names the arm.
        engine.SubmitConsoleLine("joinclass random");
        nav.Close("tf2classmenu");
    }

    /// <summary>Stock's two verbs, as the server's <c>ClientCommand</c>
    /// arms name them (tf_player.cpp:7482-7491).</summary>
    internal const string OpenedVerb = "menuopen";

    internal const string ClosedVerb = "menuclosed";

    /// <summary>Whether a show said <c>menuopen</c> and no close has
    /// answered it: the gate that keeps the word to one per close.</summary>
    private bool _toldOpen;

    public void Opened()
    {
        engine.SubmitConsoleLine(OpenedVerb);
        _toldOpen = true;
    }

    public void Closed()
    {
        if (!_toldOpen)
            return;
        _toldOpen = false;
        engine.SubmitConsoleLine(ClosedVerb);
    }

    /// <summary>Cancel hides the menu without choosing (stock's Cancel is
    /// only ever visible with a class already chosen, :1044-1081).</summary>
    public void Cancel() => nav.Close("tf2classmenu");

    /// <summary>Edit Loadout is stock's <c>openloadout</c>
    /// (tf_classmenu.cpp:1307-1320): the loadout rises on the class the
    /// menu is on — here the spawned class, which stock's OnTick keeps
    /// these arms keyed on (:1138-1142) — and the ask for its reply goes
    /// with it. No class, no screen.</summary>
    public void EditLoadout()
    {
        int cls = SpawnedClass;
        if (cls <= 0)
            return;
        nav.Open("tf2loadout");
        loadout.Select(cls);
    }

    /// <summary>Reset clears the class's every slot: the
    /// <c>tf2_equip &lt;class&gt; all none</c> line (TFEquipCommand's all
    /// arm). Stock's resetclass is highlander-only and its button ships
    /// hidden (tf_classmenu.cpp:1300-1306,
    /// classselection.res:783-801): docs/divergences.md.</summary>
    public void ResetLoadout()
    {
        int cls = SpawnedClass;
        if (cls <= 0)
            return;
        engine.SubmitConsoleLine($"tf2_equip {TFClassNames.Raw((TFClass)cls)} all none");
    }

    /// <summary><c>CTFGameRules::GetClassLimit</c> as the client can read
    /// it: one of each under <c>mp_highlander</c>, else
    /// <c>tf_classlimit</c>; zero is stock's no limit.</summary>
    private int Limit => highlander.Value ? 1 : Math.Max(0, classLimit.Value);

    /// <summary>Teammates on this class, the local player not counted
    /// (stock's class count minus one, :1492-1498; and
    /// <c>CanPlayerChooseClass</c> works the same way, so the count
    /// both tiles show and the arm that refuses to send come from here).</summary>
    private int Count(int classNumber)
    {
        if (Core is not { } core || Tf is not { } tf)
            return 0;
        int team = Team;
        if (team < 2)
            return 0;
        int local = screen.LocalPlayerIndex;
        var coreState = core.State;
        var tfState = tf.State;
        int n = 0;
        for (int slot = 1; slot < Slots; slot++)
        {
            if (slot == local)
                continue;
            if (coreState.HasFlag(slot, PlayerResourceNetState.FlagConnected) && coreState.Team[slot] == team
                && tfState.PlayerClass[slot] == classNumber)
                n++;
        }
        return n;
    }

    private int SpawnedClass
    {
        get
        {
            int local = screen.LocalPlayerIndex;
            return Tf is { } tf && local is > 0 and < Slots ? tf.State.PlayerClass[local] : 0;
        }
    }

    /// <summary>The core player resource's rows — the team and the
    /// connected flag — re-found if it has gone (the team menu's way).</summary>
    private ClientPlayerResource? Core
    {
        get
        {
            if (_core is null || !standalones.Live().Contains(_core))
                _core = standalones.OfClass(EntityNames.PlayerManager)
                                    .OfType<ClientPlayerResource>().FirstOrDefault();
            return _core;
        }
    }

    /// <summary>The TF player resource's rows — the class — re-found the
    /// same way.</summary>
    private ClientTFPlayerResource? Tf
    {
        get
        {
            if (_tf is null || !standalones.Live().Contains(_tf))
                _tf = standalones.OfClass(TFEntityNames.PlayerResource)
                                  .OfType<ClientTFPlayerResource>().FirstOrDefault();
            return _tf;
        }
    }
}

/// <summary>The <c>changeclass</c> command (stock's client
/// <c>CON_COMMAND</c>, tf_viewport.cpp:148-213): shows the class panel,
/// and only for a player on a game team — stock's switch shows the panel
/// by the team's own name (its spectator arm is arena-only, and arena and
/// MvM are this port's deferred modes), so the named show is what this
/// asks for: the menu then wears that team, as stock's did.</summary>
[ConCommand("changeclass", Help = "Choose a new class")]
public sealed class TFChangeClassCommand(ITFClassMenuSource source, ITFMenuNavigation nav) : IConCommand
{
    public void Execute(string args)
    {
        // Stock's iTeam == TF_TEAM_RED ? PANEL_CLASS_RED : PANEL_CLASS_BLUE.
        if (source.Team is 2 or 3)
            nav.OpenPanel(source.Team == 2 ? "class_red" : "class_blue");
    }
}
