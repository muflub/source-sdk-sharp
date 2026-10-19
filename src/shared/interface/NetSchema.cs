using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// What the server has to say about one entity's two paths
/// (plan_nettable_generic.md Part 14 Step 3).
///
/// <para>
/// FOUR states, not a bool, because "they agree", "they differ", "this entity
/// has no payload" and "this build cannot answer" are four different facts and
/// three of them are not failures. A bool would have collapsed the last two into
/// "disagrees", which is the one answer that must mean something — a watch loop
/// that cried wolf on every prop_physics would be turned off within the hour.
/// </para>
/// </summary>
public enum PayloadAgreementState
{
    /// <summary>Native does not expose the check. An older build, not a result.</summary>
    NotSupplied = 0,

    /// <summary>The entity carries no payload props. Most entities.</summary>
    NoPayload = 1,

    /// <summary>Every payload word reads the bytes the typed props read.</summary>
    Agrees = 2,

    /// <summary>At least one does not. The only answer worth acting on.</summary>
    Disagrees = 3,
}

/// <summary>
/// The answer, with the two numbers that say where to look when it is bad.
/// </summary>
/// <param name="Words">Payload props on the entity's table; 0 when it has none.</param>
/// <param name="FirstBadWord">
/// The first word that disagreed, or -1. A word index rather than a byte offset
/// because that is what the two sides share: the blob bases differ between
/// client and server by construction, so an offset means nothing across the
/// pair and an index means everything.
/// </param>
public readonly record struct PayloadAgreement(
    PayloadAgreementState State, int Words, int FirstBadWord);
