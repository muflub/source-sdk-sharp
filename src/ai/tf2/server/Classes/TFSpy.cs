using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Spy's own (plan_bots_teams_tf2 C.3, C.3.1, P7): its play (<see cref="TFDecider.ClassPlay"/>'s members it
/// overrides), its ability rows, its archetypes and whether the composition may field it. The Spy lane's file:
/// another class's lane never edits it, and it never edits another's.
/// </summary>
public static class TFSpyClass
{
    /// <summary>
    /// Whether the composition fields the Spy (C.4): yes. On ctf_2fort, 4 pinned Backstabbers against 4 pinned Soldiers on the
    /// same names (tools/ai-tf-class, 300 s, 3 runs, build 0e8a92216): first on the enemy's flag at 45, 43 and 41 s from the
    /// round's start against 99, 88 and 77 s; stuck lines 35, 52 and 37 against 39, 30 and 153; no Spy stood still in a
    /// fight over 10 s (the stands over 10 s were 11 to 15 s in the shared Escort and Guard leaves).
    /// </summary>
    public const bool Ready = true;

    /// <summary>The rows' names: a disguise per class (<see cref="DisguiseRow"/>), the last disguise, the watch's key and the sapper's verb.</summary>
    public const string LastDisguise = "spy_lastdisguise", Cloak = "spy_cloak", Sap = "spy_sap";

    /// <summary>The disguise row of <paramref name="dress"/>: <c>disguise &lt;class&gt; -1</c>, the enemy's team (stock's client numbers).</summary>
    public static string DisguiseRow(TFClass dress) => $"spy_disguise_{TFClassNames.Raw(dress)}";

    /// <summary>The classes a disguise row is made for, in the class numbers' order (Scout 1 to Engineer 9).</summary>
    public static ImmutableArray<TFClass> Dresses() =>
        [TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman, TFClass.Medic, TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer];

    /// <summary>The number of disguise rows: one per class from Scout to Engineer.</summary>
    public const int DressCount = 9;

    /// <summary>Commands the watch's key is held: two, a press a player makes (the Dead Ringer's arm is the same key).</summary>
    public const int CloakTicks = 2;

    /// <summary>
    /// The Spy's ability rows, appended in its own block of the one table (<see cref="TFClassSpecs.BlockOf"/>): a row
    /// added here moves no other row, so recordings keep their meaning. Add at the end only.
    /// </summary>
    public static ImmutableArray<AbilityRow> Abilities() =>
    [
        .. Dresses().Select(c => new AbilityRow(DisguiseRow(c), AbilityRun.Command, $"{TFSpyDisguiseCommand.Verb} {(int)c} -1")),
        new AbilityRow(LastDisguise, AbilityRun.Command, TFSpyLastDisguiseCommand.Verb),
        new AbilityRow(Cloak, AbilityRun.Buttons, Buttons: (int)InputButtons.Attack2, HoldTicks: CloakTicks),
        new AbilityRow(Sap, AbilityRun.Command, TFSapperCommand.Verb),
    ];

    /// <summary>
    /// The archetypes' names (the Spy research §2, the review's order: the stock set, the Saboteur on stock items, then the
    /// Dead Ringer and the Gunspy).
    /// </summary>
    public const string Backstabber = "Backstabber", Saboteur = "Saboteur", DeadRinger = "DeadRinger", Gunspy = "Gunspy";

    /// <summary>The unlocks the later archetypes name: the Ambassador, the Dead Ringer, the Big Earner.</summary>
    public const int Ambassador = 61, DeadRingerWatch = 59, BigEarner = 461;

    /// <summary>The stock kit: Revolver 24, Knife 4, Invis Watch 30, Sapper 735, each standing for its reskins.</summary>
    private static ImmutableArray<TFArchetypeSlot> Stock() =>
        [new(TFLoadoutSlot.Secondary, [24]), new(TFLoadoutSlot.Melee, [4]), new(TFLoadoutSlot.Pda2, [30]), new(TFLoadoutSlot.Building, [735])];

    /// <summary>
    /// The Spy's archetypes (C.3.1), drawn by the bot's seed and weight. The watch and the knife are always named: a slot
    /// the row left to the draw could hand a Backstabber a Dead Ringer or a Kunai, which play another way (research §4.2).
    /// </summary>
    public static TFArchetypeTable Archetypes() => new(TFClass.Spy,
    [
        new TFArchetype(Backstabber, 0.40f, Stock()),
        new TFArchetype(Saboteur, 0.15f, Stock()),
        new TFArchetype(DeadRinger, 0.25f, [new(TFLoadoutSlot.Secondary, [Ambassador]), new(TFLoadoutSlot.Melee, [4]),
                                            new(TFLoadoutSlot.Pda2, [DeadRingerWatch]), new(TFLoadoutSlot.Building, [735])]),
        new TFArchetype(Gunspy, 0.10f, [new(TFLoadoutSlot.Secondary, [Ambassador]), new(TFLoadoutSlot.Melee, [BigEarner]),
                                        new(TFLoadoutSlot.Pda2, [30]), new(TFLoadoutSlot.Building, [735])]),
    ]);

    /// <summary>What the foundation composes: the class, its ready flag, its rows, its archetypes.</summary>
    public static TFClassSpec Spec() => new(TFClass.Spy, Ready, Abilities(), Archetypes());
}

/// <summary>
/// The Spy's own HUD (the Spy research §3.3's missing rows, every one the bot's own screen): the cloak meter 0..100
/// (<c>spy.cloak</c>), whether it wears a disguise (<c>spy.disguised</c>) or one is going on (<c>spy.disguising</c>),
/// the class it shows (<c>spy.dress</c>), whether it is cloaked (<c>spy.cloaked</c>) and whether the Dead Ringer is armed
/// (<c>spy.feign</c>), off the bot's own networked state and nobody else's: a reader of ITFPlayerData the perception census
/// lists (review F6: read through a static in the view, it hid from the census).
/// </summary>
// Registered as itself: it joins the ITFHudMeters chain as every hook does. As = [typeof(ITFHudMeters)], as the foundation's
// instructions say, registers the interface a second time beside TFMedigunMeters and refuses the composition.
[SideService(Side.Server)]
public sealed class TFSpyMeters(ITFPlayerData players) : ITFHudMeters, IOrderedHook
{
    /// <summary>
    /// Its place in the readers' chain: 100 and the Spy's class number (the coordinator's notice 1). A chain refuses two
    /// members at one order, and unordered it stood at 0 beside TFMedigunMeters: live, every sense tick threw and no bot
    /// ever joined a team.
    /// </summary>
    public int HookOrder => 100 + (int)TFClass.Spy;

    public const string CloakMeter = "spy.cloak", Disguised = "spy.disguised", Disguising = "spy.disguising", Dress = "spy.dress",
                        Cloaked = "spy.cloaked", Feign = "spy.feign";

    public void Read(EntityHandle bot, TFClass playerClass, List<TFMeter> into)
    {
        // The bot's own state alone: its index, never another's.
        if (playerClass != TFClass.Spy || players.Networked(bot.EntryIndex) is not { } own)
            return;
        bool disguised = own.InCond(TFCond.Disguised);
        into.Add(new TFMeter(CloakMeter, own.CloakMeter));
        into.Add(new TFMeter(Disguised, disguised ? 1f : 0f));
        into.Add(new TFMeter(Disguising, own.InCond(TFCond.Disguising) ? 1f : 0f));
        into.Add(new TFMeter(Dress, disguised ? own.DisguiseClass : 0));
        into.Add(new TFMeter(Cloaked, own.InCond(TFCond.Stealthed) ? 1f : 0f));
        into.Add(new TFMeter(Feign, own.FeignDeathReady ? 1f : 0f));
    }
}
