using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a per-client transmit rule tells CheckTransmit to do with an entity
/// for one recipient.
/// </summary>
public enum TransmitAnswer
{
    /// <summary>No opinion: the entity is sent by the ordinary PVS check, the
    /// answer an entity with no rule gives.</summary>
    Default,

    /// <summary>Sent to this recipient whatever the PVS says (stock's
    /// ShouldTransmit answering FL_EDICT_ALWAYS).</summary>
    Always,

    /// <summary>Not sent to this recipient, whatever the PVS says.</summary>
    Never,
}

/// <summary>
/// One mod's per-recipient exception to the transmit walk, asked as a chain
/// from <c>ClientsBinding.ServerShouldTransmit</c> for every entity the
/// engine FULLCHECKs — the ones the IShimNetworkServer
/// EntitySetTransmitFullCheck slot opted in, whose fate stock farmed out to
/// the entity's own <c>ShouldTransmit(pInfo)</c>
/// (CServerGameEnts::CheckTransmit, gameinterface.cpp). Every rule is asked;
/// a <see cref="TransmitAnswer.Never"/> beats an
/// <see cref="TransmitAnswer.Always"/>; with no rule, or only defaults, the
/// ordinary PVS check decides, so a mod that registers nothing leaves the
/// walk exactly as stock's flag matrix answers it.
/// </summary>
public interface ITransmitRule
{
    /// <summary>Whether the engine sends <paramref name="entity"/> to the client whose edict is <paramref name="recipientSlot"/>.</summary>
    TransmitAnswer ShouldTransmit(EntityHandle entity, int recipientSlot);
}
