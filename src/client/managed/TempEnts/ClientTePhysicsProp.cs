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
/// <c>CTEPhysicsProp</c>, played: c_te_physicsprop.cpp's
/// <c>C_TEPhysicsProp::PostDataUpdate</c>,
/// <code>
/// tempents-&gt;PhysicsProp( m_nModelIndex, m_nSkin, m_vecOrigin, m_angRotation,
///                        m_vecVelocity, m_nFlags, m_nEffects );
/// </code>
/// which is <see cref="ClientPhysicsProps.PhysicsProp"/>: a clientside physics
/// prop, or with bit 0 of the flags its break pieces (stock's server sends it
/// so from CBreakableProp::Break in multiplayer). The tool recording
/// (RecordPhysicsProp) is not carried: this build runs no client tools.
///
/// <para>
/// With no client vphysics (the ragdolls' calls did not bind) there is no prop
/// to make, and that is said once per level rather than drawn as nothing.
/// </para>
/// </summary>
[ClientTempEnt("CTEPhysicsProp")]
public sealed class ClientTePhysicsProp(ILog log, ClientPhysicsProps? props = null)
    : ClientTempEnt<TePhysicsProp>, IClientTempEntLevel
{
    private bool _said;

    public void LevelInit()
    {
        _said = false;
    }

    public override void Play(TePhysicsProp te)
    {
        if (props is not null)
        {
            props.PhysicsProp(te.ModelIndex, te.Skin, te.Origin, QAngle.FromVector(te.Rotation), te.Velocity,
                              te.Flags, te.Effects);
            return;
        }

        if (_said)
            return;

        _said = true;
        log.Warning("[SourceSharp/Client] CTEPhysicsProp arrived with no client vphysics to make the prop in "
                  + $"(model {te.ModelIndex}, skin {te.Skin}, flags {te.Flags}); nothing is drawn. Said once per level.\n");
    }
}
