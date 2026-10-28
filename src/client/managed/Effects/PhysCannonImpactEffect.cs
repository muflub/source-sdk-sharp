//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>DECLARE_CLIENT_EFFECT( "PhyscannonImpact", CallbackPhyscannonImpact )</c>
/// (weapon_physcannon.cpp, HL2MP): the physcannon's blast. The carrier's own
/// view gets the gauss explosion where the blast landed and a short beam from
/// the gun's first attachment to it; everyone else sees the explosion at the
/// gun.
///
/// <para>
/// The server names the CARRIER (its entindex) rather than stock's weapon
/// entity, because the carried weapon here is one per player: the gun is the
/// carrier's viewmodel for the local player and its world model otherwise.
/// The first-person beam starts where the world's projection draws the
/// viewmodel's muzzle (stock's beam asks the viewmodel, whose attachments are
/// FormatViewModelAttachment's): <see cref="IViewModelAttachmentFormat"/>.
/// </para>
/// </summary>
public sealed class PhysCannonImpactEffect(IClientSparks sparks, IClientBeams beams,
                                           IClientStandalones standalones, IEngineEntityRender render,
                                           IEngineHud screen, IViewModelAttachmentFormat viewModels) : IClientEffectHandler
{
    /// <summary>PHYSCANNON_BEAM_SPRITE.</summary>
    public const string BeamSprite = "sprites/orangelight1.vmt";

    public string EffectName => "PhyscannonImpact";

    public void Dispatch(in EffectData data)
    {
        int owner = data.EntIndex;
        if (owner <= 0)
            return;

        // Do special first-person fix-up: the attachment entity is the viewmodel.
        bool local = owner == screen.LocalPlayerIndex;
        ClientStandaloneEntity? gun = Gun(local ? EntityNames.PredictedViewModel : EntityNames.SsWeapon, owner);

        if (gun is null || !render.GetAttachment(gun.Handle, 1, out Vector attachment, out _))
            return;

        Vector dir = (data.Origin - attachment).Normalized;

        if (!local)
        {
            // Explosion at the starting point
            sparks.GaussExplosion(attachment, dir, 0);
            return;
        }

        // Explosions at the impact point
        sparks.GaussExplosion(data.Origin, dir * -1f, 0);

        // Draw a beam: a tenth of a second, 12 wide to 4, white, still, by the
        // sprite's name as stock's BeamInfo_t names it.
        beams.CreateBeamPoints(Beam(viewModels.ForWorldPass(gun.Handle, attachment), data.Origin));
    }

    /// <summary>The blast's BeamInfo_t, from the gun to where it landed.</summary>
    public static BeamInfo Beam(Vector from, Vector to) => new()
    {
        Start = from,
        End = to,
        ModelName = BeamSprite,
        HaloScale = 0f,
        Life = 0.1f,
        Width = 12f,
        EndWidth = 4f,
        FadeLength = 0f,
        Amplitude = 0f,
        Brightness = 255f,
        Speed = 0f,
        StartFrame = 0,
        FrameRate = 30f,
        Red = 255f,
        Green = 255f,
        Blue = 255f,
        Segments = 16,
        Renderable = true,
        Flags = BeamFlags.OnlyNoiseOnce,
    };

    private ClientStandaloneEntity? Gun(string className, int owner)
    {
        foreach (var entity in standalones.OfClass(className))
        {
            if (entity is ClientSsViewModel vm && vm.State.OwnerIndex == owner)
                return vm;

            if (entity is ClientSsWeapon weapon && weapon.State.OwnerIndex == owner)
                return weapon;
        }

        return null;
    }
}
