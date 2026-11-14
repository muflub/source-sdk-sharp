using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Gear;
using TF2.Descent.Tests.Hero;

namespace TF2.Descent.Tests.OwnerViews;

/// <summary>
/// The UI3 server pieces over <see cref="EquipmentRig"/>'s unhosted hero: the owner's item,
/// sheet and skill senders on the ledger's and the hero's hooks, the hero's verbs, and a
/// recording wire for what was sent to whom.
/// </summary>
internal sealed class OwnerRig
{
    public const int Index = EquipmentRig.Index;

    public readonly EquipmentRig Gear;
    public readonly RecordingEngine Wire = new();
    public readonly RecordingEngine Notices = new();
    public readonly FixedDamage Damage = new();
    public readonly OwnerItemSender Items;
    public readonly OwnerSheetSender Sheet;
    public readonly OwnerSkillSender Skills;
    public readonly OwnerClientCommands Commands;

    public OwnerRig(bool join = true)
    {
        Gear = new EquipmentRig(join: false);
        Items = new OwnerItemSender(Wire, Gear.Ledger, Gear.Keys, Gear.Hero.Heroes, Gear.Equipment, new OwnerItemViews(Rules), Rules);
        Sheet = new OwnerSheetSender(Wire, Gear.Hero.Heroes, new HeroRig.ListHooks<IHeroContributionSource>(Gear.Hero.Contributions),
                                     Damage, Rules);
        Skills = new OwnerSkillSender(Wire, Gear.Hero.Heroes, Rules);
        Gear.LedgerHooks.Add(Items);
        Gear.Hero.Watchers.Add(Items);
        Gear.Hero.Watchers.Add(Sheet);
        Gear.Hero.Watchers.Add(Skills);
        Commands = new OwnerClientCommands(Gear.Hero.Heroes, Gear.Keys, Gear.Equipment, Gear.Ledger, Gear.Hero.Engine,
                                           new ClientMessages(Notices), Rules, Gear.Log);
        if (join)
            Gear.Join();
    }

    public TF2DescentRules Rules => Gear.Rules;

    public HeroComponent Hero => Gear.HeroComponent;

    public static EntityHandle Player => EquipmentRig.Player;

    /// <summary>The hero raised to <paramref name="level"/> by the rules (points come with it).</summary>
    public void Level(int level) => Hero.Apply(s => Gear.Hero.AtLevel(s, level));

    /// <summary>Every user message sent: its key, the player it went to, its bytes.</summary>
    public IReadOnlyList<(string Key, int Player, byte[] Bytes)> Sent =>
        [.. Wire.Calls.Where(c => c.Member == "UserMessages.Send").Select(c => (c.ArgAs<string>(0), c.ArgAs<int>(2), c.ArgAs<byte[]>(4)))];

    public static string KeyOf<T>() where T : IUserMessagePayload<T> => T.Key;

    /// <summary>The payloads of <typeparamref name="T"/> sent, in order, from <paramref name="from"/> on.</summary>
    public IReadOnlyList<T> Of<T>(int from = 0) where T : IUserMessagePayload<T>
    {
        string key = KeyOf<T>();
        return [.. Sent.Skip(from).Where(s => s.Key == key).Select(s =>
        {
            var reader = new UserMessageReader(s.Bytes);
            return T.Read(ref reader);
        })];
    }

    /// <summary>The notices the hero's verbs told: their text and argument.</summary>
    public IReadOnlyList<(string Text, string Arg)> Told =>
        [.. Of<Notice>(Notices).Select(n => (n.Text, n.Arg))];

    private static IReadOnlyList<T> Of<T>(RecordingEngine wire) where T : IUserMessagePayload<T> =>
        [.. wire.Calls.Where(c => c.Member == "UserMessages.Send" && c.ArgAs<string>(0) == KeyOf<T>()).Select(c =>
        {
            var reader = new UserMessageReader(c.ArgAs<byte[]>(4));
            return T.Read(ref reader);
        })];

    /// <summary>A client command from the hero.</summary>
    public bool Command(string verb, params string[] args) => Commands.Handle(Player, verb, args);

    /// <summary>A damage range the fact sets.</summary>
    internal sealed class FixedDamage : IHeroWeaponDamage
    {
        public (int Min, int Max)? Range { get; set; } = (40, 120);

        public (int Min, int Max)? RangeOf(HeroComponent hero) => Range;
    }
}
