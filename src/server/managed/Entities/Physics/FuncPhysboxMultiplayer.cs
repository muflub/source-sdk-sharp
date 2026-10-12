using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A <see cref="FuncPhysbox"/> under a second classname, and in this build that
/// is all it is.
///
/// <para>
/// Stock's CPhysBoxMultiplayer is CPhysBox plus three things, and none of the
/// three survives the crossing. It implements IMultiplayerPhysics; it sets
/// COLLISION_GROUP_PUSHAWAY in Activate; and it networks m_iPhysicsMode and
/// m_fMass. Each is deliberately absent rather than overlooked, so each is
/// written down here.
/// </para>
///
/// <para>
/// <b>The interface has no live reader.</b> Every consumer of
/// IMultiplayerPhysics sits in obstacle_pushaway.cpp, inside AvoidPushawayProps
/// and PerformObstaclePushaway. PerformObstaclePushaway is called by nothing;
/// AvoidPushawayProps is called only by CBasePlayer::AvoidPhysicsProps, which is
/// itself called by nothing in src/. The interface that made this class worth
/// having is reached by no code path in this build, so an entity implementing it
/// would be answering a question nobody asks.
/// </para>
///
/// <para>
/// <b>The collision group is left off on purpose, and it is the one choice here
/// that changes behaviour.</b> COLLISION_GROUP_PUSHAWAY means "non-solid to the
/// player, and the player's own move code shoves it aside instead" — the two
/// halves are a pair, and only the first half is live. CGameRules::ShouldCollide
/// drops PLAYER against PUSHAWAY, its HL2MP guard is not defined for this build,
/// and util_shared.cpp puts that question to this hierarchy as well as to the
/// SDK's. The second half is the pushaway code above, which never runs. So
/// reproducing the group would buy exactly one observable change: a crate you
/// walk straight through, with nothing pushing it aside. A physbox you can
/// collide with is both the more useful entity and the one stock was describing
/// before its other half died, so the group is not reproduced.
/// </para>
///
/// <para>
/// <b>The two networked values are not networked.</b> m_iPhysicsMode is never
/// assigned on the server for this class — no constructor, no keyvalue, unlike
/// its prop sibling — so it is a constant zero on the wire, and m_fMass only
/// mirrors what VPhysics already knows. Both are read solely through
/// IMultiplayerPhysics, which is the dead path above. The standalone blob is one
/// shared size across every class on the hierarchy, so a field declared here is
/// paid for by every entity in the map; two that nothing reads are not worth
/// that toll.
/// </para>
///
/// <para>
/// What is left is the base class, which is the honest answer: a map naming
/// func_physbox_multiplayer gets a physbox that falls, collides, takes damage
/// and breaks. The entity is in neither shipped map, so this closes a row rather
/// than fixing anything.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncPhysboxMultiplayer, ScriptedEntityKind.Brush)]
public sealed class FuncPhysboxMultiplayer : FuncPhysbox
{
    public FuncPhysboxMultiplayer(IEngineEntityPhysics physics)
        : base(physics)
    {
    }

}
