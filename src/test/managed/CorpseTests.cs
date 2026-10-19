extern alias client;
using SavedEntity = client::SourceSharp.Abi.SavedEntity;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A player dies and leaves a body — <see cref="Corpses"/>,
/// <see cref="SsRagdoll"/> and <see cref="ClientSsRagdoll"/>, the three halves
/// of stock's <c>CreateRagdollEntity</c> / <c>hl2mp_ragdoll</c> /
/// <c>C_HL2MPRagdoll</c>.
///
/// <para>
/// WHAT THESE ARE REALLY GUARDING is the reason a corpse is its own entity.
/// Respawning reuses the player's handle, so the tempting implementation —
/// ragdoll the player where they stand — produces a body that stands up and
/// runs away three seconds later. That failure is invisible to any test that
/// only asks "did a ragdoll appear", so the tests below ask WHICH ENTITY
/// became one.
/// </para>
/// </summary>
public class CorpseTests
{
    private const string Model = "models/humans/group03/male_01.mdl";

    /// <summary>What the model answers for an idle activity in these tests.</summary>
    private const int IdleSequence = 4;

    /// <summary>The dying player's entity index, as the client will resolve it.</summary>
    private const int PlayerIndex = 3;


    private static readonly EntityHandle Player = new(7u);
    private static readonly EntityHandle Body = new(99u);

    /// <summary>
    /// An engine double a corpse can be made against: it answers the four
    /// facts read off the dying player and hands back one body handle.
    /// </summary>
    private static RecordingEngine Dying(Vector origin, Vector angles, Vector velocity,
                                         string model = Model)
    {
        var engine = Fake();

        engine.Returns("EntityRender.GetModelName", model);
        engine.Returns("Entities.GetOrigin", origin);
        engine.Returns("Entities.GetAngles", angles);
        engine.Returns("Entities.GetVelocity", velocity);
        engine.Returns("Entities.Create", Body);
        engine.Returns("Entities.IsValid", true);

        // Which player the body came from, so the client can collapse it from
        // their pose. A distinct number from every handle above, so a test
        // cannot pass by confusing an index with a handle.
        engine.Returns("Entities.GetIndex", PlayerIndex);

        return engine;
    }

    /// <summary>
    /// A directory that hands back one managed instance, standing in for the
    /// registry that binds one at spawn.
    /// </summary>
    private sealed class OneBody : IEntityDirectory
    {
        private readonly ScriptedEntity? _instance;

        public OneBody(ScriptedEntity? instance) => _instance = instance;

        public ScriptedEntity? Get(EntityHandle handle) => _instance;

        // The three this test is not about, named rather than left to default
        // quietly: Corpses reaches the directory only to find the instance
        // spawning just bound, and asks nothing else of it.
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => Array.Empty<ScriptedEntity>();
        public bool IsRegistered(string className) => className == EntityNames.SsRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => Array.Empty<ScriptedClassInfo>();
    }

    private static Corpses Service(RecordingEngine engine, out SsRagdoll body)
    {
        body = new SsRagdoll(engine);

        // BOUND BY HAND because nothing here is the registry. In a game the
        // registry gives a server state its storage when the entity is created;
        // a test that skipped this would throw on the first write rather than
        // report what was written.
        body.State.BindLocal();

        return new Corpses(new RecordingLog(), engine, engine, new OneBody(body), engine);
    }

    /// <summary>
    /// The registry the body looks its dead player up in. Empty by default —
    /// most of these tests are about a machine that cannot see the player,
    /// which is the ordinary case for a body in someone else's PVS.
    /// </summary>
    private sealed class Viewers : IClientStandalones
    {
        private readonly Dictionary<int, ClientStandaloneEntity> _byIndex = new();

        public void Add(int index, ClientStandaloneEntity entity) => _byIndex[index] = entity;

        public IReadOnlyList<ClientStandaloneEntity> Live() => _byIndex.Values.ToList();

        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _byIndex.TryGetValue(entityIndex, out var e) ? e : null;
    }

    /// <summary>A client body with somewhere for its decoded state to live.</summary>
    private static ClientSsRagdoll Viewer(client::SourceSharp.Abi.RecordingEngine engine, Viewers? others = null)
    {
        // At the origin, matching the state's default -- see the note below.
        // Set on the engine rather than in each test, because every viewer test
        // reaches the placement check before anything it is actually about.
        var body = new ClientSsRagdoll(engine, others ?? new Viewers(), new RecordingRagdolls(engine));

        // The registry does both of these in a game (ClientStandaloneRegistry
        // binds the engine at create, and the decode gives the state its
        // buffer). Without the bind, Entities is the STATIC engine and the
        // double never sees where the body was put.
        body.BindEngine(engine, engine);
        body.State.BindLocal();
        body.State.PlayerIndex = -1;

        // Placed by default: State.Origin and the entity's origin are both the
        // zero vector, so these tests are about what happens AFTER the body has
        // arrived where it died. The one test about not-arrived-yet says so.
        return body;
    }

    // ---------------------------------------------------------------------
    // The service
    // ---------------------------------------------------------------------

    /// <summary>
    /// THE BODY IS A DIFFERENT ENTITY FROM THE PLAYER, which is the whole
    /// design. Asserted by handle rather than by classname: an implementation
    /// that ragdolled the player in place would also spawn nothing, and
    /// "nothing was spawned" is what this catches.
    /// </summary>
    [Fact]
    public void DyingSpawnsASeparateEntityAndNotTheDeadPlayer()
    {
        var engine = Dying(new Vector(10f, 20f, 30f), new Vector(0f, 90f, 0f), Vector.Zero);
        var corpses = Service(engine, out _);

        EntityHandle body = corpses.Make(Player);

        Assert.True(body == Body,
            $"dying spawns a body and returns it, and got {body}");

        Assert.True(body != Player,
            "the body is NOT the player's own entity. Respawning reuses the player's handle "
          + "— Players.Think calls Respawn on it — so a player turned limp in place would "
          + "stand up and run off as their own corpse. This is the assertion that fails if "
          + "someone 'simplifies' this into ragdolling the player");

        var created = engine.Last("Entities.Create")!.Value;

        Assert.True(created.ArgAs<string>(0) == EntityNames.SsRagdoll,
            $"and what it spawns is an {EntityNames.SsRagdoll}, not a "
          + $"{created.ArgAs<string>(0)}");
    }

    /// <summary>
    /// The body wears the player's model and stands where they fell, facing
    /// the way they faced. All four are read off the player rather than
    /// invented — a corpse in the wrong place is the visible failure.
    /// </summary>
    [Fact]
    public void TheBodyTakesTheDeadPlayersModelPlaceAndFacing()
    {
        var origin = new Vector(128f, -64f, 16f);
        var angles = new Vector(0f, 135f, 0f);

        var engine = Dying(origin, angles, Vector.Zero);
        var corpses = Service(engine, out _);

        corpses.Make(Player);

        var model = engine.Last("EntityRender.SetModel")!.Value;
        var placed = engine.Last("Entities.SetOrigin")!.Value;
        var faced = engine.Last("Entities.SetAngles")!.Value;

        Assert.True(model.ArgAs<EntityHandle>(0) == Body && model.ArgAs<string>(1) == Model,
            $"the body wears the player's own model, and got '{model.ArgAs<string>(1)}' — "
          + "not a constant, because a player wearing anything else would leave somebody "
          + "else's corpse");

        Assert.True(placed.ArgAs<Vector>(1).Equals(origin),
            $"it stands where the player fell ({placed.ArgAs<Vector>(1)}, wanted {origin})");

        Assert.True(faced.ArgAs<Vector>(1).Equals(angles),
            $"facing the way they faced ({faced.ArgAs<Vector>(1)}, wanted {angles})");
    }

    /// <summary>
    /// MODEL, PLACE AND FACING ARE SET BEFORE THE SPAWN. Stated as an ordering
    /// assertion because the values would be recorded either way: spawning is
    /// what reads them, so the same three calls after it produce a body at the
    /// origin with no model, and every by-value assertion above still passes.
    /// </summary>
    [Fact]
    public void TheBodyIsDressedBeforeItIsSpawned()
    {
        var engine = Dying(new Vector(1f, 2f, 3f), Vector.Zero, Vector.Zero);
        var corpses = Service(engine, out _);

        corpses.Make(Player);

        var order = engine.Calls.Select(c => c.Member).ToList();

        int spawn = order.IndexOf("Entities.Spawn");

        Assert.True(spawn >= 0, "the body is spawned and not merely created");

        foreach (string before in new[] { "EntityRender.SetModel", "Entities.SetOrigin",
                                          "Entities.SetAngles" })
        {
            int at = order.IndexOf(before);

            Assert.True(at >= 0 && at < spawn,
                $"{before} happens BEFORE the spawn (at {at}, spawn at {spawn}) — spawning is "
              + "what reads what was set beforehand, which is why creation and spawning are "
              + "two calls at all");
        }
    }

    /// <summary>
    /// The death velocity reaches the body, so a player who was running when
    /// they died keeps going. Set AFTER the spawn, because spawning is what
    /// binds the managed instance there is a State on.
    /// </summary>
    [Fact]
    public void TheBodyCarriesTheVelocityThePlayerDiedWith()
    {
        var velocity = new Vector(300f, -50f, 120f);

        var engine = Dying(Vector.Zero, Vector.Zero, velocity);
        var corpses = Service(engine, out SsRagdoll body);

        corpses.Make(Player);

        Assert.True(body.State.Force.Equals(velocity),
            $"the body is thrown with the velocity the player died carrying, and got "
          + $"{body.State.Force}");

        Assert.True(body.State.PlayerIndex == PlayerIndex,
            $"and it records WHOSE body it is ({body.State.PlayerIndex}), which is what lets the "
          + "client collapse it from the pose they died in rather than from the model's "
          + "reference skeleton");

        Assert.True(body.State.ForceBone == -1,
            $"and at the centre of mass ({body.State.ForceBone}), because this mod's damage "
          + "path carries no per-hit bone — naming one would be inventing it");
    }

    /// <summary>
    /// -1 IS A VALUE AND NOT AN ABSENCE, and it has to survive the wire as
    /// one. A bone index declared unsigned would carry 65535 to the client,
    /// which is not a bone, and the far side would ask the engine to throw a
    /// body by a limb it does not have.
    /// </summary>
    [Fact]
    public void TheForceBoneIsSignedSoTheCentreOfMassSentinelSurvives()
    {
        var field = typeof(RagdollNetState).GetProperty("ForceBone");

        Assert.True(field is not null, "the ragdoll's table declares a ForceBone");

        Assert.True(field!.PropertyType == typeof(short),
            $"and declares it as a signed short, not {field.PropertyType.Name}. -1 means "
          + "the centre of mass and is the ordinary case, so an unsigned width sends 65535 "
          + "— a bone no model has");

        var state = new RagdollNetState();
        state.BindLocal();
        state.ForceBone = -1;

        Assert.True(state.ForceBone == -1,
            $"and -1 round-trips through the property as -1, not {state.ForceBone}");
    }

    /// <summary>
    /// A player with no model leaves no body, and says so. Refused loudly
    /// because the alternative fails silently three machines away:
    /// BecomeRagdoll declines a model it cannot find and the corpse just
    /// stands there.
    /// </summary>
    [Fact]
    public void APlayerWithNoModelLeavesNoBody()
    {
        var engine = Dying(Vector.Zero, Vector.Zero, Vector.Zero, model: "");
        var corpses = Service(engine, out _);

        EntityHandle body = corpses.Make(Player);

        Assert.True(body == EntityHandle.None, $"no body is made, and got {body}");

        Assert.True(engine.CountOf("Entities.Create") == 0,
            "and nothing is spawned at all — an entity created and then abandoned would be "
          + "an edict leaked per death");
    }

    /// <summary>Turning bodies off spawns nothing. The switch has to actually switch.</summary>
    [Fact]
    public void BodiesCanBeTurnedOff()
    {
        var engine = Dying(Vector.Zero, Vector.Zero, Vector.Zero);
        var corpses = Service(engine, out _);

        corpses.Enabled = false;

        Assert.True(corpses.Make(Player) == EntityHandle.None, "no body when they are off");

        Assert.True(engine.CountOf("Entities.Create") == 0,
            "and nothing was created, rather than created and thrown away");

        Assert.True(engine.CountOf("EntityRender.GetModelName") == 0,
            "and the player was not even asked what they look like — the switch is checked "
          + "first, so turning bodies off costs nothing per death");
    }

    /// <summary>
    /// THE OLDEST BODY GOES WHEN THE CAP IS REACHED. A timer alone does not
    /// bound this: a firefight can pile up far more bodies inside one lifetime
    /// than a map has players.
    /// </summary>
    [Fact]
    public void TheOldestBodyIsRemovedWhenTheCapIsReached()
    {
        var engine = Dying(Vector.Zero, Vector.Zero, Vector.Zero);
        var corpses = Service(engine, out _);

        corpses.Limit = 3;

        for (int i = 0; i < 3; i++)
            corpses.Make(Player);

        Assert.True(corpses.Count == 3, $"three bodies fit under a cap of three ({corpses.Count})");

        Assert.True(engine.CountOf("Entities.Remove") == 0,
            "and none of them was removed to make room, because there was room");

        corpses.Make(Player);

        Assert.True(engine.CountOf("Entities.Remove") == 1,
            $"the fourth removes exactly one body ({engine.CountOf("Entities.Remove")}) — the "
          + "oldest");

        Assert.True(corpses.Count == 3,
            $"and the count stays at the cap ({corpses.Count}) rather than growing");
    }

    /// <summary>
    /// A level change forgets every body. Handles do not survive one, and a
    /// kept handle may since belong to something else entirely — which the cap
    /// would then remove.
    /// </summary>
    [Fact]
    public void ALevelChangeForgetsEveryBody()
    {
        var engine = Dying(Vector.Zero, Vector.Zero, Vector.Zero);
        var corpses = Service(engine, out _);

        corpses.Make(Player);
        corpses.Make(Player);

        Assert.True(corpses.Count == 2, $"two bodies stand ({corpses.Count})");

        corpses.Reset();

        Assert.True(corpses.Count == 0,
            $"and a level change forgets them ({corpses.Count}) — a handle kept across one "
          + "may since belong to something else, which the cap would then remove");
    }

    // ---------------------------------------------------------------------
    // The dead player themselves
    // ---------------------------------------------------------------------

    /// <summary>
    /// THE DEAD PLAYER STOPS BEING DRAWN, and this is not cosmetic tidying.
    ///
    /// <para>
    /// The body standing there is now a separate entity, so leaving the player
    /// drawn puts two copies of the same model in the same place — plus the
    /// weapon bone-merged to the live one, which follows its skeleton. Three
    /// overlapping models read as a single badly deformed one, which is exactly
    /// how this was first reported.
    /// </para>
    ///
    /// <para>
    /// Stock says the same thing in the same place: <c>Event_Killed</c>'s
    /// "since we're dead, it won't draw us on the client". Stock gets it from
    /// <c>C_BasePlayer</c> declining to draw a dead player; the standalone
    /// player has no such rule, so it is said out loud.
    /// </para>
    /// </summary>
    [Fact]
    public void DyingStopsTheDeadPlayerBeingDrawn()
    {
        var engine = Fake();
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid());
        player.BindAreas(Areas(engine));   // dying makes the player not solid (A22)

        player.State.BindLocal();

        player.OnKilled(EntityHandle.None);

        var call = engine.Last("EntityRender.SetVisible")!.Value;

        Assert.True(!call.ArgAs<bool>(1),
            "a dead player stops being drawn — the corpse is a separate entity, and both drawn "
          + "in the same place is what a deformed body actually was");

        Assert.True(engine.CountOf("Entities.Remove") == 0,
            "and they are NOT removed: it is the same entity that respawns, and the observer "
          + "camera the death cycle hands out rides on it");
    }

    /// <summary>
    /// AND IS DRAWN AGAIN WHEN THEY COME BACK. Spawn is the respawn on this
    /// hierarchy — <c>PlayerSequences.Respawn</c> calls straight into it — so
    /// this is the only place that undoes the hide, and a player who came back
    /// invisible would be a worse bug than the one it fixes.
    /// </summary>
    [Fact]
    public void RespawningDrawsThePlayerAgain()
    {
        var engine = Fake();
        engine.Ignores("Entities.GetOrigin", "Entities.GetHealth");

        // The spawn now asks the engine for a physics shadow (plan_entity.md
        // step 3f). Stubbed rather than ignored: this test is about what
        // OnSpawn draws, and a strict engine that has to be TOLD the answer is
        // what makes "OnSpawn asked for something new" visible here at all.
        engine.Returns("Shadows.Init", true);

        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid());
        player.BindAreas(Areas(engine));

        player.State.BindLocal();

        player.OnKilled(EntityHandle.None);
        player.OnSpawn();

        var call = engine.Last("EntityRender.SetVisible")!.Value;

        Assert.True(call.ArgAs<bool>(1),
            "spawning draws them again. Spawn IS the respawn here, so nothing else will undo "
          + "the hide — an invisible respawned player is the failure this pins");
    }

    // ---------------------------------------------------------------------
    // The server entity
    // ---------------------------------------------------------------------

    /// <summary>
    /// A body is NOT SOLID, and arms its own removal. The first is what stops a
    /// corpse becoming a wall in a doorway; the second is the only thing that
    /// bounds how long one lasts.
    /// </summary>
    [Fact]
    public void ABodyIsNotSolidAndArmsItsOwnRemoval()
    {
        var engine = Fake();
        var body = new SsRagdoll(engine);
        body.BindAreas(Areas(engine));

        body.OnSpawn();

        var call = engine.Last("EntityPhysics.SetNotSolid")!.Value;

        Assert.True(call.ArgAs<bool>(1),
            "a body is not solid — a corpse in a doorway would otherwise block it, and one on "
          + "a stairway would be an invisible step");

        Assert.True(body.ThinkArmed == SsRagdoll.Lifetime,
            $"and it arms its removal for its lifetime ({body.ThinkArmed}s, wanted "
          + $"{SsRagdoll.Lifetime}s). Nothing else cleans these up: this mod has neither "
          + "stock's ragdoll manager nor a round restart");
    }

    /// <summary>
    /// Its think removes it, which is the whole of what the timer buys.
    ///
    /// <para>
    /// Read off the entity rather than off the engine double, because
    /// <c>Remove</c> is a <c>ScriptedEntity</c> convenience that goes through
    /// the STATIC engine — the same wall <c>PropPhysics.Gone</c> exists for.
    /// </para>
    /// </summary>
    [Fact]
    public void ABodyRemovesItselfWhenItsTimeIsUp()
    {
        var engine = Fake();
        var body = new SsRagdoll(engine);
        body.BindAreas(Areas(engine));

        Assert.True(!body.Gone, "a fresh body is in the world");

        body.OnThink();

        Assert.True(body.Gone,
            "and its think takes it out again. The client half's OnRemoved tears the ragdoll "
          + "down on each viewer, so there is no second message to send");
    }

    // ---------------------------------------------------------------------
    // The client entity — where a ragdoll actually appears
    // ---------------------------------------------------------------------

    /// <summary>
    /// The viewer's half collapses the body, passing the impulse it was thrown
    /// with. This is the call that makes a ragdoll; everything else is
    /// arrangements for it.
    /// </summary>
    [Fact]
    public void TheViewerCollapsesTheBodyWithTheImpulseItWasThrownWith()
    {
        var force = new Vector(0f, 250f, 40f);

        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);

        var body = Viewer(engine);

        body.State.Force = force;
        body.State.ForceBone = -1;

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var call = engine.Last("EntityRender.BecomeRagdoll")!.Value;

        Assert.True(call.ArgAs<Vector>(1).Equals(force),
            $"the body is thrown with the impulse that crossed ({call.ArgAs<Vector>(1)})");

        Assert.True(call.ArgAs<int>(2) == -1,
            $"at the bone the server named ({call.ArgAs<int>(2)})");

        Assert.True(body.IsRagdoll, "and it knows it is now a ragdoll");
    }

    /// <summary>
    /// THE BODY IS POSED BEFORE IT COLLAPSES, and this is the assertion for a
    /// bug that was SEEN before it was understood: the corpse appeared as a
    /// long stretched smear across the floor.
    ///
    /// <para>
    /// A <c>.phy</c>'s constraint limits are authored against a real animation
    /// pose. The shell defaults an entity nothing has animated to sequence 0 —
    /// the reference pose, arms out — so a body collapsed straight from it
    /// starts every joint outside its limits and the first solve tears it
    /// apart. <c>BecomeRagdoll</c> returns TRUE throughout: this is a success
    /// that looks like a physics bug, which is why the ordering is asserted
    /// rather than trusted.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBodyIsPosedBeforeItIsCollapsed()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);

        var body = Viewer(engine);

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        var order = engine.Calls.Select(c => c.Member).ToList();

        int posed = order.IndexOf("EntityRender.PlaySequence");
        int collapsed = order.IndexOf("EntityRender.BecomeRagdoll");

        Assert.True(posed >= 0,
            "the body is put into a pose. Without one it collapses from the model's REFERENCE "
          + "skeleton, which is the stretched-smear bug");

        Assert.True(posed < collapsed,
            $"and it is posed BEFORE it collapses (posed at {posed}, collapsed at {collapsed}) — "
          + "afterwards is no use at all: BecomeRagdoll reads the skeleton to place every "
          + "element, and after it the pose is frozen");

        var play = engine.Last("EntityRender.PlaySequence")!.Value;

        Assert.True(play.ArgAs<int>(1) == IdleSequence,
            $"the pose is a real resolved activity ({play.ArgAs<int>(1)}), not sequence 0 — "
          + "stock falls back to 0 here and 0 is the reference pose, the very thing that "
          + "breaks");

        Assert.True(play.ArgAs<float>(2) == 0f && play.ArgAs<bool>(3),
            $"held still at the start of it (rate {play.ArgAs<float>(2)}, restart "
          + $"{play.ArgAs<bool>(3)}) — stock's SetSequence(iSeq); SetCycle(0.0)");

        Assert.True(body.PosedAs == IdleSequence,
            $"and the body says which pose it collapsed from ({body.PosedAs}), because a smear "
          + "is otherwise indistinguishable from a body");
    }

    /// <summary>
    /// IT COLLAPSES OUT OF THE POSE THE PLAYER DIED IN when this machine can
    /// see them — stock's local-player branch, which reads
    /// <c>pPlayer-&gt;GetSequence()</c> off the networked player handle. A body
    /// that always used an idle would stand every corpse up straight, whatever
    /// they were doing when they died.
    /// </summary>
    [Fact]
    public void TheBodyCollapsesOutOfThePoseThePlayerDiedIn()
    {
        const int Running = 17;

        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);
        engine.Returns("EntityRender.SequenceGroundSpeed", 200f);
        engine.Returns("EntityRender.LookupPoseParameter", -1);
        engine.Returns("Entities.GetAngles", Vector.Zero);

        // The player's body is driven by the shared machine (phase 0.10), so
        // the reads the host's EngineAnimTarget needs are stubbed the same
        // way PlayerAnimatorTests stubs them: a model worn, not dormant, no
        // dials, and no viewer (the eyes are the body).
        engine.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl");
        engine.Returns("EntityRender.IsDormant", false);
        engine.Returns("EntityRender.GetPoseParameter", 0f);
        engine.Returns("Hud.LocalPlayerIndex", 0);

        var player = new ClientSsPlayer(engine, clock: engine, screen: engine) { Handle = new EntityHandle(7u) };
        player.BindEngine(engine, engine);
        player.State.BindLocal();
        player.State.Alive = true;                    // stock's Update gate

        // The player is mid-stride: the animator resolves and remembers a
        // sequence, and that is the one the body must take.
        engine.Returns("EntityRender.LookupSequenceByActivity", Running);
        player.State.Velocity = new Vector(200f, 0f, 0f);
        player.State.MaxSpeed = 200f;
        player.State.Flags = ClientSsPlayer.OnGroundFlag;
        player.Animate();

        Assert.True(player.ShownSequence == Running,
            $"the player is animating in sequence {player.ShownSequence}");

        var others = new Viewers();
        others.Add(3, player);

        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);

        var body = Viewer(engine, others);
        body.State.PlayerIndex = 3;

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(body.PosedFromThePlayer,
            "the pose came from the player and not from the idle fallback — the two are "
          + "indistinguishable by sequence number whenever a player dies standing still, "
          + "which is exactly how a probe kills one");

        Assert.True(body.PosedAs == Running,
            $"the body collapses from the player's own pose ({body.PosedAs}, wanted {Running}) "
          + $"and not from the idle fallback ({IdleSequence}) — a corpse that always used an "
          + "idle would stand every body up straight whatever they were doing when they died");
    }

    /// <summary>
    /// THE PLAYER IT WAS POSED FROM COMES BACK ACROSS A CLIENT RELOAD, as a
    /// handle the restore re-checked against the client's own list. It is the
    /// client's one saved handle (stock's <c>m_hPlayer</c>), and while
    /// EntityIsValid was server-only the re-check answered false over here and
    /// it came back None for a player who was still there.
    /// </summary>
    [Fact]
    public void ThePlayerABodyWasPosedFromSurvivesAClientReload()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.LookupSequenceByActivity", 17);
        engine.Returns("EntityRender.SequenceGroundSpeed", 200f);
        engine.Returns("EntityRender.LookupPoseParameter", -1);
        engine.Returns("Entities.GetAngles", Vector.Zero);

        // As in the sibling fact: the body's pose now comes from the shared
        // machine, so the host's reads are stubbed and the composition's
        // clock is the fake itself.
        engine.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl");
        engine.Returns("EntityRender.IsDormant", false);
        engine.Returns("EntityRender.GetPoseParameter", 0f);
        engine.Returns("Hud.LocalPlayerIndex", 0);

        var player = new ClientSsPlayer(engine, clock: engine, screen: engine) { Handle = new EntityHandle(0x0003_8007u) };
        player.BindEngine(engine, engine);
        player.State.BindLocal();
        player.State.Alive = true;
        player.State.Velocity = new Vector(200f, 0f, 0f);
        player.State.MaxSpeed = 200f;
        player.State.Flags = ClientSsPlayer.OnGroundFlag;
        player.Animate();

        var others = new Viewers();
        others.Add(3, player);

        var body = Viewer(engine, others);
        body.State.PlayerIndex = 3;
        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(body.PosedFrom == player.Handle,
            $"the body remembers the player it was posed from (0x{body.PosedFrom.Value:X8})");

        var record = new SavedEntity(body.Handle.Value, body.ClassName, string.Empty);
        body.SaveState(record);

        // A live engine whose list still holds the player: the restore asks it.
        engine.Returns("Host.IsInitialized", true);
        engine.Returns("Entities.IsValid", true);

        var rebuilt = new ClientSsRagdoll(engine, others, new RecordingRagdolls(engine));
        rebuilt.BindEngine(engine, engine);
        rebuilt.State.BindLocal();
        rebuilt.LoadState(record);

        Assert.True(rebuilt.PosedFrom == player.Handle,
            $"and the rebuilt body has it back (0x{rebuilt.PosedFrom.Value:X8}, wanted " +
            $"0x{player.Handle.Value:X8})");
        Assert.True(engine.CountOf("Entities.IsValid") > 0, "re-checked against the client's engine");
    }

    /// <summary>
    /// AND FROM AN IDLE WHEN IT CANNOT SEE THEM, which is the ordinary case for
    /// a body in someone else's corner of the map. Stock falls back to sequence
    /// 0 here; 0 is the reference pose, so this resolves an activity instead.
    /// </summary>
    [Fact]
    public void ABodyWhosePlayerThisMachineCannotSeeStillGetsARealPose()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);

        var body = Viewer(engine);
        body.State.PlayerIndex = 42;      // nobody by that index on this client

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(body.PosedAs == IdleSequence && !body.PosedFromThePlayer,
            $"an unseen player falls back to a resolved idle ({body.PosedAs}), which is still a "
          + "real pose — the player being dormant or out of PVS is ordinary, not a failure — "
          + "and it says which of the two it used");

        Assert.True(body.IsRagdoll, "and the body still collapses");
    }

    /// <summary>
    /// A model that answers to no activity at all is LEFT ALONE rather than
    /// forced to sequence 0. It has no pose to collapse from, and
    /// <c>BecomeRagdoll</c> refuses a model with no sequences anyway — forcing
    /// one would only hide that.
    /// </summary>
    [Fact]
    public void AModelWithNoAnimationIsNotForcedIntoSequenceZero()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", false);
        engine.Returns("EntityRender.LookupSequenceByActivity", -1);

        var body = Viewer(engine);

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(body.PosedAs == -1, $"nothing resolved, and it says so ({body.PosedAs})");

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "and no sequence was written. Sequence 0 is the reference pose — forcing it is "
          + "how the smear happens, so a model with no animation is left as it is");
    }

    /// <summary>
    /// ONE BODY, HOWEVER MANY UPDATES ARRIVE. A second RagdollCreate over a
    /// live one leaks every object and constraint in the first, and a corpse
    /// gets an update every time the server sends one.
    /// </summary>
    [Fact]
    public void TheBodyCollapsesOnceHoweverManyUpdatesArrive()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", true);
        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);

        var body = Viewer(engine);

        for (int i = 0; i < 5; i++)
            body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(engine.CountOf("EntityRender.BecomeRagdoll") == 1,
            $"the engine is asked exactly once "
          + $"({engine.CountOf("EntityRender.BecomeRagdoll")} times) — a second create over a "
          + "live ragdoll leaks every object and constraint in the first");

        Assert.True(body.Attempts == 1,
            $"and it stopped trying after the one that worked ({body.Attempts} attempts)");
    }

    /// <summary>
    /// IT KEEPS ASKING UNTIL ONE TAKES, which is the one place this does more
    /// than stock. The model can arrive after the entity does (ClientSsPlayer
    /// records the same race), and BecomeRagdoll needs the model to answer at
    /// all — so a create-only attempt that lost that race would leave a body
    /// standing rigidly to attention forever with nothing saying why.
    /// </summary>
    [Fact]
    public void ABodyWhoseModelHasNotArrivedYetIsAskedAgain()
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("EntityRender.BecomeRagdoll", false);
        engine.Returns("EntityRender.LookupSequenceByActivity", IdleSequence);

        var body = Viewer(engine);

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.True(!body.IsRagdoll, "a refused body is not a ragdoll");

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(engine.CountOf("EntityRender.BecomeRagdoll") == 3,
            $"and it is asked again on each update "
          + $"({engine.CountOf("EntityRender.BecomeRagdoll")}), rather than once on CREATED "
          + "and never again — the model can arrive after the entity does");

        engine.Returns("EntityRender.BecomeRagdoll", true);

        body.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.True(body.IsRagdoll,
            "and the moment the model is there, the body collapses");
    }
}

/// <summary>
/// The retirement rules <see cref="Corpses"/> gained when lane p4-anim moved
/// <c>CRagdollLRURetirement</c> and <c>CRagdollLowViolenceManager</c> out of
/// <c>game/shared/ragdoll_shared.cpp</c>.
///
/// <para>
/// These are about the SERVICE and not about the policy — the policy's own
/// rules are in <see cref="RagdollRetirementPolicyTests"/>. What is under test
/// here is that <see cref="Corpses"/> actually asks it, and that the answer
/// reaches the engine.
/// </para>
/// </summary>
public class CorpseRetirementTests
{
    private static readonly EntityHandle Player = new(7u);
    private static readonly EntityHandle Body = new(99u);

    private static RecordingEngine Engine()
    {
        var engine = TestServices.Fake();

        engine.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl");
        engine.Returns("Entities.GetOrigin", Vector.Zero);
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetVelocity", Vector.Zero);
        engine.Returns("Entities.Create", Body);
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.GetIndex", 3);

        return engine;
    }

    private sealed class NoDirectory : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => Array.Empty<ScriptedEntity>();
        public IEnumerable<ScriptedEntity> All() => Array.Empty<ScriptedEntity>();
        public bool IsRegistered(string className) => className == EntityNames.SsRagdoll;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => Array.Empty<ScriptedClassInfo>();
    }

    private static Corpses Service(RecordingEngine engine)
        => new(new RecordingLog(), engine, engine, new NoDirectory(), engine);

    [Fact]
    public void ACapOfZeroMakesNoBodyAtAllRatherThanOneItSweepsNextFrame()
    {
        var engine = Engine();
        var corpses = Service(engine);

        corpses.Limit = 0;

        Assert.Equal(EntityHandle.None, corpses.Make(Player));
        Assert.Equal(0, corpses.Count);
        Assert.Equal(0, engine.CountOf("Entities.Create"));
    }

    [Fact]
    public void ACapOfOneKeepsExactlyOneBody()
    {
        var engine = Engine();
        var corpses = Service(engine);

        corpses.Limit = 1;

        corpses.Make(Player);
        corpses.Make(Player);
        corpses.Make(Player);

        Assert.Equal(1, corpses.Count);
        Assert.Equal(2, engine.CountOf("Entities.Remove"));
    }

    /// <summary>
    /// THE VACUITY, PINNED RATHER THAN ASSUMED. Every one of the four legacy
    /// ConVars reads "0" — which is <c>UTIL_IsLowViolence</c>'s own trigger —
    /// and the server still answers false, because stock's multiplayer clause
    /// clears it and this mod is always multiplayer. If somebody ever removes
    /// that clause thinking it dead, this fact is what says the removal
    /// changed something.
    /// </summary>
    [Fact]
    public void TheServerAnswersNotLowViolenceEvenWithEveryViolenceConVarOff()
    {
        var engine = Engine();
        engine.Returns("Console.FindConVarValue", "0");

        var corpses = Service(engine);
        ((IGameLifecycle)corpses).OnLevelInit("dm_lockdown");

        Assert.False(corpses.LowViolence);
    }

    /// <summary>
    /// And the four ConVars ARE read, which the fact above cannot show on its
    /// own: an implementation that never called <c>FindConVarValue</c> would
    /// answer false too.
    /// </summary>
    [Fact]
    public void TheFourViolenceConVarsAreActuallyRead()
    {
        var engine = Engine();
        engine.Returns("Console.FindConVarValue", "1");

        var corpses = Service(engine);
        ((IGameLifecycle)corpses).OnLevelInit("dm_lockdown");

        Assert.Equal(4, engine.CountOf("Console.FindConVarValue"));
    }

    [Fact]
    public void ALevelInitWithNoConsoleStillAnswers()
    {
        var engine = Engine();
        var corpses = new Corpses(new RecordingLog(), engine, engine, new NoDirectory());

        ((IGameLifecycle)corpses).OnLevelInit("dm_lockdown");

        Assert.False(corpses.LowViolence);
    }
}
