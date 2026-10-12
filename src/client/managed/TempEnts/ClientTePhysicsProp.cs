//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEPhysicsProp</c>, played. A clientside physics prop, thrown from the
/// server as a temp entity so that the debris costs no edict.
///
/// <para>
/// The whole of <c>c_te_physicsprop.cpp</c>'s
/// <c>C_TEPhysicsProp::PostDataUpdate</c>:
/// </para>
/// <code>
/// tempents-&gt;PhysicsProp( m_nModelIndex, m_nSkin, m_vecOrigin, m_angRotation,
///                        m_vecVelocity, m_nFlags, m_nEffects );
/// </code>
///
/// <para>
/// <b>THE NATIVE HELPER IS ALREADY A DOCUMENTED NO-OP, AND THIS CLASS INHERITS
/// THAT RATHER THAN CAUSING IT.</b> <c>CTempEnts::PhysicsProp</c> in
/// <c>c_te_legacytempents.cpp</c> ends in <c>return NULL</c> with a paragraph
/// above it saying why: the body built a <c>C_PhysPropClientside</c> and drove
/// it through Spawn / SetHealth(0) / Break, and <c>physpropclientside.cpp</c> is
/// <c>-$File</c> out of <c>client_csharp.vpc</c> — the clientside prop
/// simulation this build has is C#'s, and there is nothing for that door to
/// open onto. It was shut before this lane reached it.
/// </para>
///
/// <para>
/// <b>AND NOTHING SENDS IT EITHER.</b> The only two callers of
/// <c>te-&gt;PhysicsProp</c> in the whole tree are
/// <c>game/server/props.cpp:1775</c> and <c>:1784</c>, and <c>props.cpp</c> is
/// excluded from <c>server_csharp.vpc</c> because <c>prop_physics_multiplayer</c>
/// is managed. So the message is never put on the wire, and this class has
/// never once been reached. The survey checked before claiming it —
/// <c>docs/te-legacy-pool-survey.md</c> section 2 says "confirm nothing sends it
/// before claiming it", and this is that confirmation written down.
/// </para>
///
/// <para>
/// <b>SO WHY PORT IT AT ALL, AND WHY IS IT NOT A LEDGER ROW.</b> Ported because
/// plan 0.2b requires the two sides' class counts to come out equal, and
/// because the SERVER half is managed already — a native client half for a
/// managed server class is exactly the asymmetry this phase exists to remove.
/// Not a ledger row because nothing was lost here by anything: the capability
/// was gone before the port, the native no-op that records it is still
/// compiled, and this class does not delete a call that exposed anything. What
/// it does instead is SAY it, once per level, which the native no-op did not —
/// it answered NULL in silence.
/// </para>
///
/// <para>
/// The alternative was an empty <c>Play</c>, and it was rejected for the reason
/// this port keeps running into: a player that does nothing and a player that
/// works look identical from the console, and one of the two is a bug.
/// </para>
/// </summary>
[ClientTempEnt("CTEPhysicsProp")]
public sealed class ClientTePhysicsProp(ILog log) : ClientTempEnt<TePhysicsProp>, IClientTempEntLevel
{
    private bool _said;

    public void LevelInit()
    {
        _said = false;
    }

    public override void Play(TePhysicsProp te)
    {
        // DECODED IN FULL, and then not used, which is deliberate. If this ever
        // arrives it is news -- the sender is excluded from this build -- and
        // the first question will be what it carried. A player that read
        // nothing could not answer it.

        PropIsMissing(te.Origin, te.Rotation, te.Velocity, te.ModelIndex, te.Skin, te.Flags, te.Effects);
    }

    /// <summary>
    /// This build has no clientside physics prop, said out loud once per level.
    /// </summary>
    private void PropIsMissing(Vector origin, Vector rotation, Vector velocity,
                               int modelIndex, int skin, int flags, int effects)
    {
        if (_said)
            return;

        _said = true;

        log.Warning(
            "[SourceSharp/Client] CTEPhysicsProp arrived, which is news: nothing in "
          + "this build sends it (game/server/props.cpp is excluded). There is no "
          + "clientside physics prop to make either -- physpropclientside.cpp is "
          + "excluded too, and CTempEnts::PhysicsProp has answered NULL since before "
          + $"this port reached it. Carried: model {modelIndex}, skin {skin}, flags "
          + $"{flags}, effects {effects}, at ({origin.X:F0} {origin.Y:F0} {origin.Z:F0}) "
          + $"angles ({rotation.X:F0} {rotation.Y:F0} {rotation.Z:F0}) velocity "
          + $"({velocity.X:F0} {velocity.Y:F0} {velocity.Z:F0}). Said once per level.\n");
    }
}
