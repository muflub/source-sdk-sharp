//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What ss_barrel networks beyond what every entity on the hierarchy does.
///
/// <para>
/// The first class to have a table of its own, and it is the barrel for the
/// same reason the barrel exists at all: it is the entity whose job is to prove
/// the wire rather than to be played with. Every field type native can carry is
/// here, so a wrong offset shows up as ONE field wrong rather than as nothing at
/// all — which is the failure mode a datatable has and nothing else does.
/// </para>
///
/// <para>
/// This is the WHOLE declaration. The backing fields, the accessors that write
/// through to the entity's blob, and the decode that fills them from a received
/// one are written by SourceSharp.Generators — see NetStateGenerator for what
/// that buys and what it deliberately does not do.
/// </para>
///
/// <para>
/// Declared here, in shared code, and referenced by neither half of the entity
/// by name: the server's <c>TestBarrel</c> and the client's
/// <c>ClientTestBarrel</c> are different types in different assemblies, so a
/// declaration on each would be two lists to keep in step. Both hold one of
/// THIS.
/// </para>
///
/// <para>
/// DECLARATION ORDER IS THE WIRE FORMAT. Appending is safe; inserting in the
/// middle renumbers every offset after it, on both sides at once.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsBarrel)]
public partial class BarrelNetState
{
    /// <summary>
    /// How many times it has thought. An integer that visibly moves.
    ///
    /// A <c>short</c>, which is how C# already says "sixteen bits" — so there is
    /// no bit count beside it that has to agree with it, and the blob stores two
    /// bytes rather than four.
    /// </summary>
    public partial short Thinks { get; set; }

    /// <summary>
    /// The last hit it took, after its own damage rule.
    ///
    /// [Predicted] with stock's fall-velocity tolerance (c_baseplayer.cpp:351),
    /// for the same reason everything else is on this class: the barrel proves
    /// the wire, and since 9c3 that includes proving that a declared tolerance
    /// travels from this attribute to the client's misprediction meter. Nothing
    /// predicts a barrel; the tolerance only shapes how its meter reads.
    /// </summary>
    [Predicted(0.5f)]
    public partial float LastDamage { get; set; }

    /// <summary>Where the map put it. Constant, and therefore easy to check.</summary>
    public partial Vector SpawnOrigin { get; set; }

    /// <summary>Whether it has been destroyed.</summary>
    public partial bool Broken { get; set; }

    /// <summary>
    /// The model the map asked for. A STRING, which is the field type the
    /// runtime-networked entity cannot carry and this one can — and the only one
    /// whose size its C# type does not state, which is why it has an attribute
    /// and nothing else here does.
    /// </summary>
    [NetString(64)]
    public partial string NetModel { get; set; }
}
