using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server;
using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Voice;
using TF2.Tests.Entities;
using TF2.Tests.Player;

using Xunit;

using Outcome = TF2.Server.Voice.TFVoiceMenuCommand.Outcome;

namespace TF2.Tests.Voice;

/// <summary>
/// The <c>voicemenu</c> chain: the verb's rate limit, the admission gates,
/// the item's concept into the response rules, the matched scene's sounds,
/// the VoiceSubtitle audience, and the next-speak bookkeeping. Stock's shape:
/// <c>CMultiplayRules::ClientCommand</c>/<c>VoiceCommand</c>
/// (multiplay_gamerules.cpp:1637-1810) over
/// <c>CTFGameRules::VoiceCommand</c> (tf_gamerules.cpp:10166-10210).
/// </summary>
public sealed class TFVoiceMenuCommandTests
{
    private const string MenusScript =
        """
        "voicemenus"
        {
            "menu_1"
            {
                "item_1"
                {
                    "concept"                 "TLK_PLAYER_MEDIC"
                    "show_subtitle"           "1"
                    "distance_check_subtitle" "1"
                }
                "item_2"
                {
                    "concept"                 "TLK_PLAYER_THANKS"
                    "show_subtitle"           "1"
                }
            }
            "menu_2"
            {
                "item_1"
                {
                    "concept"                 "TLK_PLAYER_YES"
                }
            }
        }
        """;

    // scripts/talker/medic.txt's PlayerMedicMedic through response_rules.txt's
    // ConceptPlayerMedic and IsMedic criteria, the shapes the shipped scripts
    // spell; the shipped-content match itself is the response rules' own fact.
    private const string MedicRules =
        """
        criterion ConceptPlayerMedic Concept TLK_PLAYER_MEDIC
        criterion IsMedic playerclass Medic
        response PlayerMedicMedicResponder
        {
            scene scenes/Player/Medic/low/611.vcd
        }
        rule PlayerMedicMedic
        {
            criteria ConceptPlayerMedic IsMedic
            response PlayerMedicMedicResponder
        }
        """;

    [Fact]
    public void TheVerbIsOursAndShortFormsAreHandledSilently()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Speak, "vo/medic.ogg");

        // the verb routes; a foreign one is another hook's to take
        Assert.True(rig.Command.Handle(PlayerRig.Player, "voicemenu", ["0", "0"]));
        Assert.Equal(1, rig.Rules.Asks);
        Assert.False(rig.Command.Handle(PlayerRig.Player, "voicemenu2", ["0", "0"]));

        // fewer than two arguments is handled with no menu
        Assert.True(rig.Command.Handle(PlayerRig.Player, "voicemenu", ["0"]));
        Assert.Equal(1, rig.Rules.Asks);
    }

    [Fact]
    public void TheCommandReachesTheVoiceCommandWithTheItemsConcept()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Scene, "scenes/Player/Medic/low/611.vcd", ["vo/medic/medic.ogg"]);

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        // the item's concept, asked with the player's words behind it
        Assert.NotNull(rig.Rules.Asked);
        Assert.Equal("TLK_PLAYER_MEDIC", Value(rig.Rules.Asked!, "concept"));
        Assert.Equal("Medic", Value(rig.Rules.Asked!, "playerclass"));

        // the scene's speak event, sounded from the player
        Assert.Equal(["vo/medic/medic.ogg"],
            rig.Engine.AllOf("Sound.EntityEmitSound").Select(c => c!.ArgAs<string>(1)));
    }

    [Fact]
    public void TheMenuAndItemIndexTheScriptRows()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Speak, "vo/thanks.ogg");

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 1));
        Assert.Equal("TLK_PLAYER_THANKS", Value(rig.Rules.Asked!, "concept"));
    }

    [Fact]
    public void TheRateLimitHoldsARepeatInsideTheWindowAndFreesOnePastIt()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Speak, "vo/medic.ogg");

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        rig.Clock.Now = 10.2f;
        Assert.Equal(Outcome.RateLimited, rig.VoiceMenu(0, 0));
        Assert.Equal(1, rig.Engine.CountOf("Sound.EntityEmitSound"));

        rig.Clock.Now = 10.3f;
        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));
        Assert.Equal(2, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void SuspensionModeOneRefusesEveryVoiceCommand()
    {
        var rig = new Rig(suspension: 1);
        rig.Speak(ResponseKind.Speak, "vo/medic.ogg");

        Assert.Equal(Outcome.Refused, rig.VoiceMenu(0, 0));
        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
        Assert.Empty(rig.Engine.AllOf("UserMessages.Send"));
    }

    [Fact]
    public void TheNextSpeakGateHoldsUntilTheCappedSceneLengthPasses()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Scene, "scenes/Long.vcd", ["vo/long.ogg"], seconds: 9f); // 1.5 caps it

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        rig.Clock.Now = 11.4f;
        Assert.Equal(Outcome.Refused, rig.VoiceMenu(0, 0));

        rig.Clock.Now = 11.8f; // past the refused probe's own rate window, too
        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));
    }

    [Fact]
    public void ASpokenSceneInsideASecondOfTheLastReadsSpamAndAddsHalfASecond()
    {
        var rig = new Rig();

        // the Short scene is half a second: the first word ends at 10.5
        rig.Speak(ResponseKind.Scene, "scenes/Short.vcd", ["vo/short.ogg"]);
        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        // 0.3 past the rate limit, 0.3 inside the grace: the spam counter
        // takes it, and the window is 10.8 + 0.5 + 0.5 = 11.8
        rig.Clock.Now = 10.8f;
        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        rig.Clock.Now = 11.5f;
        Assert.Equal(Outcome.Refused, rig.VoiceMenu(0, 0));
    }

    [Fact]
    public void AnOutOfRangeMenuOrItemAndAShortCommandAreSilent()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Speak, "vo/medic.ogg");

        Assert.Equal(Outcome.Empty, rig.VoiceMenu(9, 0));
        rig.Clock.Now = 10.3f;
        Assert.Equal(Outcome.Empty, rig.VoiceMenu(0, 9));
        Assert.True(rig.Command.Handle(PlayerRig.Player, "voicemenu", ["0"]));
        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
        Assert.Equal(0, rig.Rules.Asks);
    }

    [Fact]
    public void TheMedicCriteriaMatchThePlayerMedicMedicRule()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Scene, "scenes/Player/Medic/low/611.vcd", ["vo/medic/medic.ogg"]);
        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        // the very set the command asked, against the medic-shaped rules; a
        // plain-criterion miss is only a zero score, so either half alone is
        // still a match, and no word at all is none
        var words = rig.Rules.Asked!;
        Assert.Equal("PlayerMedicMedic", rig.MedicWords(words).Rule);

        var classOnly = new ResponseCriteriaSet();
        classOnly.Append("playerclass", "Medic");
        Assert.Equal("PlayerMedicMedic", rig.MedicWords(classOnly).Rule);

        var neither = new ResponseCriteriaSet();
        Assert.False(rig.MedicMatches(neither));
    }

    [Fact]
    public void AnUnmatchedConceptPlaysNothing()
    {
        var rig = new Rig();
        rig.Rules.Match = false;

        Assert.Equal(Outcome.Unmatched, rig.VoiceMenu(0, 0));
        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
        Assert.Empty(rig.Engine.AllOf("UserMessages.Send"));
    }

    [Fact]
    public void AGhostBoosInsteadOfSpeaking()
    {
        var rig = new Rig();
        rig.SetCond(TFCond.HalloweenGhostMode);
        rig.Speak(ResponseKind.Speak, "vo/medic.ogg");

        Assert.Equal(Outcome.Ghost, rig.VoiceMenu(0, 0));

        var boo = Assert.Single(rig.Engine.AllOf("Console.SendClientCommand"));
        Assert.Equal(PlayerRig.Player, boo!.ArgAs<EntityHandle>(0));
        Assert.Equal("boo", boo.ArgAs<string>(1));
        Assert.Equal(0, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void TheDistanceCutKeepsTheSpeakerAndNearbyTeammatesOnly()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Scene, "scenes/Player/Medic/low/611.vcd", ["vo/medic/medic.ogg"]);
        var near = rig.Mate(5, TFTeam.Red, new Vector(100, 0, 0));
        var far = rig.Mate(6, TFTeam.Red, new Vector(1901, 0, 0));
        var enemy = rig.Mate(7, TFTeam.Blue, new Vector(0, 10, 0));

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 0));

        // one copy per kept listener, the speaker's own and the near mate's;
        // the far teammate is past 1900 units and the enemy is another team
        var sent = rig.Subtitles();
        Assert.Equal(2, sent.Count);
        Assert.All(sent, s => Assert.Equal((byte)PlayerRig.Index, s.Speaker));
        Assert.All(sent, s => Assert.Equal((byte)0, s.Menu));
        Assert.All(sent, s => Assert.Equal((byte)0, s.Item));
        _ = far;
        _ = enemy;
        _ = near;
    }

    [Fact]
    public void ASubtitleWithoutTheDistanceCutGoesToTheWholeTeam()
    {
        var rig = new Rig();
        rig.Speak(ResponseKind.Speak, "vo/thanks.ogg"); // item 2: no distance cut
        rig.Mate(5, TFTeam.Red, new Vector(9000, 0, 0));
        rig.Mate(6, TFTeam.Blue, Vector.Zero);

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 1));

        Assert.Equal(2, rig.Subtitles().Count);
    }

    [Fact]
    public void ADisguisedSpyOnTheOtherTeamsColourShowsTheEnemyHisVoice()
    {
        var rig = new Rig();
        rig.SetCond(TFCond.Disguised);
        rig.Player.State.DisguiseTeam = (int)TFTeam.Blue; // a red spy showing blue
        rig.Speak(ResponseKind.Speak, "vo/thanks.ogg");
        rig.Mate(5, TFTeam.Red, Vector.Zero);
        rig.Mate(6, TFTeam.Blue, Vector.Zero);

        Assert.Equal(Outcome.Spoke, rig.VoiceMenu(0, 1));

        // ShouldShowVoiceSubtitleToEnemy: the team cut is skipped, so the
        // enemy hears it too
        Assert.Equal(3, rig.Subtitles().Count);
    }

    // -----------------------------------------------------------------

    private static string Value(ResponseCriteriaSet set, string name)
        => set.GetValue(set.Find(name));

    /// <summary>The voice world: one rig player, the shipped-shaped script, a
    /// capturing rules fake, and the recording engine for every seam.</summary>
    private sealed class Rig
    {
        public readonly PlayerRig Player = new();

        public RecordingEngine Engine => Player.Engine;
        public readonly FakeClock Clock = new() { Now = 10f }; // the player spawned earlier
        public readonly TFVoiceCommandSuspensionModeConVar Suspension = new();
        public readonly TFMaxVoiceSpeakDelayConVar SpeakDelay = new();
        public readonly TFVoiceCommandMenus Menus;
        public readonly TFSceneImage Scenes;
        public readonly CapturingRules Rules = new();
        public readonly FakeTeams Teams = new();
        public readonly Roster Records = new();
        public readonly Origins World;
        public readonly TFVoiceMenuCommand Command;

        public Rig(int suspension = 0)
        {
            var bytes = TFSceneImageTests.Image(
                ("scenes/Player/Medic/low/611.vcd", 2500, ["vo/medic/medic.ogg"]),
                ("scenes/Long.vcd", 9000, ["vo/long.ogg"]),
                ("scenes/Short.vcd", 500, ["vo/short.ogg"]));
            Scenes = new TFSceneImage(TFSceneImageTests.Files(("scenes/scenes.image", bytes)));
            var files = TFSceneImageTests.Files(("scripts/voicecommands.txt", Encoding.UTF8.GetBytes(MenusScript)));
            Menus = new TFVoiceCommandMenus(files, Player.Log);

            Suspension.Bind(-1, "tf_voice_command_suspension_mode", suspension.ToString());
            SpeakDelay.Bind(-1, "tf_max_voice_speak_delay", "1.5");
            World = new Origins(Player.Engine);

            Teams.SetTeam((int)TFTeam.Red, PlayerRig.Player);
            Command = new TFVoiceMenuCommand(Menus, Scenes, Rules, Player.Carriers, Records, Teams,
                World, Player.Engine, Player.Engine, Player.Engine, Clock, Suspension, SpeakDelay,
                round: null, log: Player.Log);
            Player.Join();
            Player.As(TFClass.Medic);
            Records.Add(PlayerRig.Player); // the speaker is a recipient too
        }

        /// <summary>Stages the fake rules' answer for the next speak.</summary>
        public void Speak(ResponseKind kind, string name, string[]? sounds = null, float seconds = 1f)
        {
            Rules.Match = true;
            Rules.Kind = kind;
            Rules.Name = name;
            Rules.Sounds = sounds ?? [];
            Rules.Seconds = seconds;
        }

        /// <summary>The command, as the client's <c>voicemenu m i</c> word.</summary>
        public Outcome VoiceMenu(int menu, int item) => Command.VoiceMenu(PlayerRig.Player, menu, item);

        /// <summary>Sets a condition on the rig player's carrier.</summary>
        public void SetCond(TFCond cond)
        {
            var mask = Player.State.Conditions();
            mask.Set(cond);
            Player.State.SetConditions(mask);
        }

        /// <summary>A second player on a team, at an origin.</summary>
        public EntityHandle Mate(int index, TFTeam team, Vector? at = null)
        {
            var handle = new EntityHandle(0x0001_0000u | (uint)index);
            Player.Join(index);
            Records.Add(handle);
            Teams.SetTeam((int)team, Teams.Players((int)team).Append(handle).ToArray());
            if (at is { } origin)
                World.At(handle, origin);
            return handle;
        }

        /// <summary>The subtitle payloads sent, decoded.</summary>
        public List<TFVoiceSubtitle> Subtitles()
        {
            var list = new List<TFVoiceSubtitle>();
            foreach (var c in Player.Engine.AllOf("UserMessages.Send"))
            {
                if (c!.ArgAs<string>(0) != "TF2:VoiceSubtitle")
                    continue;

                var reader = new UserMessageReader(c.ArgAs<byte[]>(4));
                list.Add(TFVoiceSubtitle.Read(ref reader));
            }

            return list;
        }

        /// <summary>The captured criteria, through the medic-shaped rule set.</summary>
        public MatchedResponse MedicWords(ResponseCriteriaSet words)
            => Medic().FindBestResponse(words, out MatchedResponse matched) ? matched : new();

        public bool MedicMatches(ResponseCriteriaSet words) => Medic().FindBestResponse(words, out _);

        private ResponseRuleSet Medic()
        {
            var rules = new ResponseRuleSet(new EvenRandom(), null);
            Assert.True(rules.Load("scripts/talker/medic.txt",
                TFSceneImageTests.Files(("scripts/talker/medic.txt", Encoding.UTF8.GetBytes(MedicRules)))));
            return rules;
        }
    }

    /// <summary>Draws nothing: the medic rule has one response to pick.</summary>
    private sealed class EvenRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => (min + max) / 2f;

        public int RandomInt(int min, int max) => min;
    }

    /// <summary>The rules seam, capturing what the command asks and answering
    /// what the test staged.</summary>
    private sealed class CapturingRules : IResponseRules
    {
        public ResponseCriteriaSet? Asked;

        public int Asks;

        public bool Match;

        public ResponseKind Kind = ResponseKind.None;

        public string Name = string.Empty;

        public string[] Sounds = [];

        public float Seconds = 1f;

        public bool FindBestResponse(ResponseCriteriaSet criteria, out MatchedResponse response, IResponseFilter? filter = null)
        {
            Asked = criteria;
            Asks++;

            // a staged scene's sounds are the image's to answer, so the fake
            // defers: the hook asks TFSceneImage, which the rig loaded for real
            response = !Match || Kind == ResponseKind.None
                ? new MatchedResponse { Kind = ResponseKind.None }
                : new MatchedResponse { Kind = Kind, Name = Name, Rule = "staged" };
            return Match && Kind != ResponseKind.None;
        }
    }

    /// <summary>Two players, both recordable.</summary>
    private sealed class Roster : IPlayerDirectory
    {
        private readonly Dictionary<uint, PlayerRecord> _all = [];

        public PlayerRecord this[EntityHandle player] => At(player);

        /// <summary>Registers a player the directory now knows.</summary>
        public void Add(EntityHandle player) => At(player);

        private PlayerRecord At(EntityHandle player)
        {
            if (!_all.TryGetValue(player.Value, out var record))
                _all[player.Value] = record = new PlayerRecord();
            return record;
        }

        public bool Knows(EntityHandle player) => _all.ContainsKey(player.Value);

        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All
            => _all.Select(kv => (new EntityHandle(kv.Key), kv.Value));

        public float WaitRemaining(EntityHandle player, float now) => 0f;

        public float RespawnDelay { get; set; }

        public ObserverMode DeathView { get; set; }

        public bool DropWeaponOnDeath { get; set; }

        public float DropSpeed { get; set; }

        public void OnSpawned(EntityHandle player, float now) { }

        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }

        public void Forget(EntityHandle player) => _all.Remove(player.Value);
    }

    /// <summary>The entity seam, the engine's reads with origins the test set.</summary>
    private sealed class Origins(IEngineEntities inner) : IEngineEntities
    {
        private readonly Dictionary<uint, Vector> _at = new();

        public void At(EntityHandle who, Vector where) => _at[who.Value] = where;

        public Vector GetOrigin(EntityHandle h) => _at.TryGetValue(h.Value, out Vector v) ? v : inner.GetOrigin(h);

        public int Count() => inner.Count();
        public void PurgeMapRefs() => inner.PurgeMapRefs();
        public void SetClassName(EntityHandle h, string className) => inner.SetClassName(h, className);
        public void UseTriggerBounds(EntityHandle entity, bool enable, float bloat, bool uniform = false) => inner.UseTriggerBounds(entity, enable, bloat, uniform);
        public void CleanupDeleteList() => inner.CleanupDeleteList();
        public int InvalidateMdlCacheAll() => inner.InvalidateMdlCacheAll();
        public int UpdateVisibilityAll() => inner.UpdateVisibilityAll();
        public IInterpolationBlocks InterpolationBlocks => inner.InterpolationBlocks;
        public IRenderPolicyBlocks RenderPolicyBlocks => inner.RenderPolicyBlocks;
        public IClientReplayEngine ClientReplay => inner.ClientReplay;

        // every other read is the recording engine's
        public EntityHandle Create(string className) => inner.Create(className);
        public EntityHandle CreateFromMap(string className, byte[] packedKeys) => inner.CreateFromMap(className, packedKeys);
        public EntityHandle CreateFromMapAt(string className, byte[] packedKeys, int edictIndex) => inner.CreateFromMapAt(className, packedKeys, edictIndex);
        public void Spawn(EntityHandle entity) => inner.Spawn(entity);
        public void AddBaseVelocity(EntityHandle entity, Vector velocity) => inner.AddBaseVelocity(entity, velocity);
        public void PositionChanged(EntityHandle entity, Vector previousOrigin) => inner.PositionChanged(entity, previousOrigin);
        public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => inner.ApplyVelocityImpulse(h, impulse, clearGround);
        public IEnumerable<EntityHandle> FindAllByClassname(string className) => inner.FindAllByClassname(className);
        public List<EntityHandle> FindAllByName(string name, int limit = 512) => inner.FindAllByName(name, limit);
        public EntityHandle FindByName(string name) => inner.FindByName(name);
        public bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default) => inner.FireInput(target, input, value, activator);
        public Vector GetAngles(EntityHandle h) => inner.GetAngles(h);
        public Vector GetAngularVelocity(EntityHandle entity) => inner.GetAngularVelocity(entity);
        public int GetArmor(EntityHandle entity) => inner.GetArmor(entity);
        public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => inner.GetBounds(entity);
        public Vector GetBoundsSize(EntityHandle entity) => inner.GetBoundsSize(entity);
        public string GetClassName(EntityHandle h) => inner.GetClassName(h);
        public Vector GetEyeAngles(EntityHandle h) => inner.GetEyeAngles(h);
        public Vector GetEyePosition(EntityHandle h) => inner.GetEyePosition(h);
        public int GetHealth(EntityHandle h) => inner.GetHealth(h);
        public int GetIndex(EntityHandle h) => inner.GetIndex(h);
        public string GetName(EntityHandle entity) => inner.GetName(entity);
        public Vector GetSpawnAngles(EntityHandle entity) => inner.GetSpawnAngles(entity);
        public int GetSpawnFlags(EntityHandle entity) => inner.GetSpawnFlags(entity);
        public Vector GetSpawnOrigin(EntityHandle entity) => inner.GetSpawnOrigin(entity);
        public Vector GetVelocity(EntityHandle h) => inner.GetVelocity(h);
        public Vector GetWorldSpaceCenter(EntityHandle h) => inner.GetWorldSpaceCenter(h);
        public bool IsPlayer(EntityHandle h) => inner.IsPlayer(h);
        public bool IsValid(EntityHandle h) => inner.IsValid(h);
        public void Remove(EntityHandle entity) => inner.Remove(entity);
        public void SetAngles(EntityHandle entity, Vector angles) => inner.SetAngles(entity, angles);
        public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => inner.SetAngularVelocity(entity, degreesPerSecond);
        public void SetArmor(EntityHandle entity, int armor) => inner.SetArmor(entity, armor);
        public void SetHealth(EntityHandle h, int health) => inner.SetHealth(h, health);
        public bool SetKeyValue(EntityHandle entity, string key, string value) => inner.SetKeyValue(entity, key, value);
        public void SetOrigin(EntityHandle h, Vector origin) => inner.SetOrigin(h, origin);
        public void SetOwner(EntityHandle h, EntityHandle owner) => inner.SetOwner(h, owner);
        public void SetVelocity(EntityHandle h, Vector velocity) => inner.SetVelocity(h, velocity);
        public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => inner.Teleport(h, origin, angles, zeroVelocity);
        public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => inner.SetParent(child, parent, parentAttachment);
        public EntityHandle GetParent(EntityHandle child) => inner.GetParent(child);
        public int GetParentAttachment(EntityHandle child) => inner.GetParentAttachment(child);
        public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => inner.SetLocalTransform(child, origin, angles);
        public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles) => inner.GetLocalTransform(child, out origin, out angles);
        public ParentProbe ParentProbe(EntityHandle entity) => inner.ParentProbe(entity);
        public bool GetAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles) => inner.GetAttachmentFrame(entity, attachment, out origin, out angles);
        public int LookupAttachmentAny(EntityHandle entity, string attachmentName) => inner.LookupAttachmentAny(entity, attachmentName);
        public EntityHandle FindBaseByName(string name) => inner.FindBaseByName(name);
        public Span<byte> GetEngineBlock(EntityHandle entity) => inner.GetEngineBlock(entity);
        public void DestroyPhysics(EntityHandle entity) => inner.DestroyPhysics(entity);
        public float GetGravity(EntityHandle entity) => inner.GetGravity(entity);
        public void SetGravity(EntityHandle entity, float gravity) => inner.SetGravity(entity, gravity);
        public SolidFlags GetSolidFlags(EntityHandle entity) => inner.GetSolidFlags(entity);
        public SolidType GetSolidType(EntityHandle entity) => inner.GetSolidType(entity);
        public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => inner.SetSolidity(entity, solidType, solidFlags);
        public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => inner.SetBounds(entity, mins, maxs);
        public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => inner.SetSpawnFlags(entity, spawnFlags);
    }

    /// <summary>Teams as a dictionary; every unknown player reads unassigned.</summary>
    private sealed class FakeTeams : ITeams
    {
        private readonly Dictionary<int, List<EntityHandle>> _byTeam = [];

        public IReadOnlyList<EntityHandle> Players(int team) => _byTeam.TryGetValue(team, out var list) ? list : [];

        public int Count(int team) => Players(team).Count;

        public int Team(EntityHandle player)
            => _byTeam.FirstOrDefault(kv => kv.Value.Contains(player)).Key;

        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();

        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;

        public void SetTeam(int team, params EntityHandle[] players) => _byTeam[team] = [.. players];
    }
}
