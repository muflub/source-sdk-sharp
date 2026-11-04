using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;
using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The gib rule and its death walk: <c>CTFPlayer::ShouldGib</c>'s arms
/// (tf_player.cpp:10914-10975) and the <c>m_bGib</c> the death leaves on the
/// body (tf_player.cpp:15619).
/// </summary>
public sealed class TFPlayerGibTests
{
    private static readonly EntityHandle Body = new(0x0100_0002u);

    private sealed class Directory(ScriptedEntity? body) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => handle == Body ? body : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == EntityNames.Hl2mpRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>A convar backend that stores what it is told, so a SetValue round-trips.</summary>
    private sealed class Backend : IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    private static TFPlayerGib Hook(ScriptedEntity? body, TFDamageRecords records, IEngineEntities entities,
                                    out ConVarRegistry convars)
    {
        convars = new ConVarRegistry(new Backend());
        convars.RegisterTypes([typeof(TFPlayerGibConVar)]);
        return new TFPlayerGib(convars.Declared<TFPlayerGibConVar>(), records, entities, new Directory(body));
    }

    private static SsRagdoll Corpse()
    {
        var engine = Mock();
        var body = new SsRagdoll(engine) { Handle = Body };
        body.BindEngine(engine);
        body.State.BindLocal();
        return body;
    }

    private static TFHitRecord Wound(DamageType bits, TFCritType crit = TFCritType.None,
                                     TFDamageCustom custom = TFDamageCustom.None)
        => new(EntityHandle.None, 40f, bits, crit, custom, TFWeaponId.None, "", 1f);

    // ---- the rule ----------------------------------------------------------

    [Fact]
    public void Playergib_zero_never_gibs_even_a_blast()
    {
        Assert.False(TFPlayerGibRule.ShouldGib(0, DamageType.Blast, TFCritType.Full,
                                               TFDamageCustom.Croc, -50));
    }

    [Fact]
    public void Playergib_two_always_gibs_even_a_featherweight_blow()
    {
        Assert.True(TFPlayerGibRule.ShouldGib(2, DamageType.None, TFCritType.None,
                                              TFDamageCustom.None, 100));
    }

    [Fact]
    public void The_explodes_suicide_bit_gibs_past_the_blast_gate()
    {
        // tf_player.cpp:10930, before the hard-hit gate: AlwaysGib without
        // Blast is the explode command's own mark (PlayerCheatCommands.Explode).
        Assert.True(TFPlayerGibRule.ShouldGib(1, DamageType.AlwaysGib, TFCritType.None,
                                              TFDamageCustom.None, 100));
    }

    [Fact]
    public void A_croc_hit_gibs_without_being_a_blast()
    {
        Assert.True(TFPlayerGibRule.ShouldGib(1, DamageType.Slash, TFCritType.None,
                                              TFDamageCustom.Croc, 100));
    }

    [Fact]
    public void A_blunt_death_at_positive_health_leaves_a_body()
    {
        Assert.False(TFPlayerGibRule.ShouldGib(1, DamageType.Bullet, TFCritType.None,
                                               TFDamageCustom.None, 3));
    }

    [Fact]
    public void A_crit_blast_gibs()
    {
        Assert.True(TFPlayerGibRule.ShouldGib(1, DamageType.Blast, TFCritType.Full,
                                              TFDamageCustom.None, 30));
    }

    [Fact]
    public void A_blast_past_the_gib_threshold_gibs()
    {
        Assert.True(TFPlayerGibRule.ShouldGib(1, DamageType.Blast, TFCritType.None,
                                              TFDamageCustom.None, -12));    // :10963's -10
    }

    // ---- the death walk ----------------------------------------------------

    [Fact]
    public void A_gibbing_death_marks_the_body()
    {
        var body = Corpse();
        var records = new TFDamageRecords();
        records.Note(3, Wound(DamageType.Blast, TFCritType.Mini), damager: true);
        var engine = Mock();
        engine.Returns("Entities.GetHealth", 45);
        var hook = Hook(body, records, engine, out _);

        hook.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), Body));

        Assert.True(body.State.Gib);
    }

    [Fact]
    public void A_plain_death_leaves_the_body_whole_and_the_default_mode_stands()
    {
        var body = Corpse();
        var records = new TFDamageRecords();
        records.Note(3, Wound(DamageType.Bullet), damager: false);
        var engine = Mock();
        engine.Returns("Entities.GetHealth", 2);
        var hook = Hook(body, records, engine, out _);   // no SetValue: the declared 1

        hook.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), Body));

        Assert.False(body.State.Gib);
    }

    [Fact]
    public void A_death_the_damage_layer_never_saw_leaves_the_body_whole()
    {
        var body = Corpse();
        var engine = Mock();
        engine.Returns("Entities.GetHealth", 2);
        var hook = Hook(body, new TFDamageRecords(), engine, out _);

        hook.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), Body));

        Assert.False(body.State.Gib);
    }

    [Fact]
    public void A_death_with_no_body_has_nothing_to_mark()
    {
        var engine = Mock();
        var hook = Hook(null, new TFDamageRecords(), engine, out _);

        hook.OnPlayerDied(new PlayerDeath(Body, 3, EntityHandle.None, default(Vector), EntityHandle.None));
    }
}
