//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A native forward's arguments, turned into the declaration it meant.
///
/// <para>
/// THIS IS THE LOGIC THAT USED TO BE IN C++. A <c>shell_te_forwards*.cpp</c> entry
/// point knew three things it had no business knowing — which temp entity class,
/// which argument becomes which field, and what the field is called — and all
/// three are here now. What is left native is the marshalling: pack the
/// arguments the signature already had, in declaration order, and hand them
/// over with an id.
/// </para>
///
/// <para>
/// SHARED, because both sides forward. The server's forwards end in a send and
/// the client's in a local play, but the mapping from arguments to a declaration
/// is the same mapping and is worth having once. What each side then does with
/// the state is its own business and lives on its own side.
/// </para>
///
/// <para>
/// THE ARGUMENTS ARRIVE POSITIONALLY, and that is the one thing this design
/// gives up against forty-two typed ABI slots. A float in the wrong index is a
/// wrong value rather than a compile error, so it is gated:
/// <c>TeForwardSignatureTests</c> reads each <c>TE_*</c> signature out of the
/// <c>.cpp</c> off disk and asserts this file's unpack takes the same count and
/// order of floats and ints — the same disk-parity check the declarations get
/// against stock's own tables.
/// </para>
/// </summary>
public static class TeForwardDispatch
{
    /// <summary>
    /// The declaration a forward meant, or null if the id is one nothing has
    /// been written for yet — or a RESERVED one (ArmorRicochet, BeamPoints,
    /// BubbleTrail, EnergySplash), whose native forwarder the server gc cull
    /// deleted, so no native code sends it.
    ///
    /// <para>
    /// NULL RATHER THAN A THROW. A forward whose arm is missing must not take
    /// the game down mid-firefight; native logs it once and sends nothing, which
    /// is the same thing an unregistered class already does.
    /// </para>
    /// </summary>
    public static TempEntState? Build(TeForward id, ReadOnlySpan<float> f, ReadOnlySpan<int> i)
        => id switch
        {
            TeForward.BSPDecal                 => BSPDecal(f, i),
            TeForward.BloodStream              => BloodStream(f, i),
            TeForward.Bubbles                  => Bubbles(f, i),
            TeForward.Decal                    => Decal(f, i),
            TeForward.Dust                     => Dust(f, i),
            TeForward.Explosion                => Explosion(f, i),
            TeForward.LargeFunnel              => LargeFunnel(f, i),
            TeForward.MetalSparks              => MetalSparks(f, i),
            TeForward.MuzzleFlash              => MuzzleFlash(f, i),
            TeForward.ProjectDecal             => ProjectDecal(f, i),
            TeForward.Smoke                    => Smoke(f, i),
            TeForward.Sparks                   => Sparks(f, i),
            TeForward.TeDispatchEffect         => TeDispatchEffect(f, i),
            TeForward.WorldDecal               => WorldDecal(f, i),
            _                         => null,
        };

    private static TempEntState? BSPDecal(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 3 || i.Length < 2)
            return null;

        return new TeBspDecal
        {
            Origin = new Vector(f[0], f[1], f[2]),
            Entity = i[0],
            Index  = i[1],
        };
    }

    private static TempEntState? BloodStream(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 6 || i.Length < 5)
            return null;

        return new TeBloodStream
        {
            Origin    = new Vector(f[0], f[1], f[2]),
            Direction = new Vector(f[3], f[4], f[5]),
            Color     = new Color32(i[0], i[1], i[2], i[3]),
            Amount    = i[4],
        };
    }

    private static TempEntState? Bubbles(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 8 || i.Length < 2)
            return null;

        return new TeBubbles
        {
            Mins       = new Vector(f[0], f[1], f[2]),
            Maxs       = new Vector(f[3], f[4], f[5]),
            ModelIndex = i[0],
            Height     = f[6],
            Count      = i[1],
            Speed      = f[7],
        };
    }

    private static TempEntState? Decal(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 6 || i.Length < 3)
            return null;

        return new TeDecal
        {
            Origin = new Vector(f[0], f[1], f[2]),
            Start  = new Vector(f[3], f[4], f[5]),
            Entity = i[0],
            Hitbox = i[1],
            Index  = i[2],
        };
    }

    private static TempEntState? Dust(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 8 || i.Length < 0)
            return null;

        return new TeDust
        {
            Origin    = new Vector(f[0], f[1], f[2]),
            Size      = f[3],
            Speed     = f[4],
            Direction = new Vector(f[5], f[6], f[7]),
        };
    }

    private static TempEntState? Explosion(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 7 || i.Length < 6)
            return null;

        return new TeExplosion
        {
            Origin       = new Vector(f[0], f[1], f[2]),
            ModelIndex   = i[0],
            Scale        = f[3],
            FrameRate    = i[1],
            Flags        = i[2],
            Normal       = new Vector(f[4], f[5], f[6]),
            MaterialType = (byte)i[3],
            Radius       = i[4],
            Magnitude    = i[5],
        };
    }

    private static TempEntState? LargeFunnel(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 3 || i.Length < 2)
            return null;

        return new TeLargeFunnel
        {
            Origin     = new Vector(f[0], f[1], f[2]),
            ModelIndex = i[0],
            Reversed   = i[1],
        };
    }

    private static TempEntState? MetalSparks(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 6 || i.Length < 0)
            return null;

        return new TeMetalSparks
        {
            Position  = new Vector(f[0], f[1], f[2]),
            Direction = new Vector(f[3], f[4], f[5]),
        };
    }

    private static TempEntState? MuzzleFlash(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 7 || i.Length < 1)
            return null;

        return new TeMuzzleFlash
        {
            Origin = new Vector(f[0], f[1], f[2]),
            Angles = new Vector(f[3], f[4], f[5]),
            Scale  = f[6],
            Type   = i[0],
        };
    }

    private static TempEntState? ProjectDecal(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 7 || i.Length < 1)
            return null;

        return new TeProjectedDecal
        {
            Origin   = new Vector(f[0], f[1], f[2]),
            Rotation = new Vector(f[3], f[4], f[5]),
            Distance = f[6],
            Index    = i[0],
        };
    }

    private static TempEntState? Smoke(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 4 || i.Length < 2)
            return null;

        return new TeSmoke
        {
            Origin     = new Vector(f[0], f[1], f[2]),
            ModelIndex = i[0],
            Scale      = f[3],
            FrameRate  = i[1],
        };
    }

    private static TempEntState? Sparks(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 6 || i.Length < 2)
            return null;

        return new TeSparks
        {
            Origin      = new Vector(f[0], f[1], f[2]),
            Magnitude   = i[0],
            TrailLength = i[1],
            Direction   = new Vector(f[3], f[4], f[5]),
        };
    }

    private static TempEntState? TeDispatchEffect(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 24 || i.Length < 12)
            return null;

        return new TeEffectDispatch
        {
            EffectData = new TeEffectData
            {
                Origin             = new Vector(f[0], f[1], f[2]),
                Start              = new Vector(f[3], f[4], f[5]),
                Angles             = new Vector(f[6], f[7], f[8]),
                Normal             = new Vector(f[9], f[10], f[11]),
                Flags              = i[0],
                Magnitude          = f[12],
                Scale              = f[13],
                AttachmentIndex    = i[1],
                SurfaceProp        = (short)i[2],
                EffectName         = i[3],
                Material           = i[4],
                DamageType         = i[5],
                HitBox             = i[6],
                EntIndex           = i[7],
                Color              = (byte)i[8],
                Radius             = f[14],
                CustomColors       = i[9] != 0,
                Color1             = new Vector(f[15], f[16], f[17]),
                Color2             = new Vector(f[18], f[19], f[20]),
                ControlPoint1      = i[10] != 0,
                ParticleAttachment = i[11],
                ControlPointOffset = new Vector(f[21], f[22], f[23]),
            },
        };
    }

    private static TempEntState? WorldDecal(ReadOnlySpan<float> f, ReadOnlySpan<int> i)
    {
        if (f.Length < 3 || i.Length < 1)
            return null;

        return new TeWorldDecal
        {
            Origin = new Vector(f[0], f[1], f[2]),
            Index  = i[0],
        };
    }

}
