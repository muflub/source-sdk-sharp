//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server's half of stock's prediction filtering: the SUPPRESS HOST
/// (<c>IPredictionSystem::SuppressHostEvents</c>, ipredictionsystem.h) and the
/// two rules that read it.
///
/// <para>
/// While a player who predicts weapons runs a command, that player is the
/// host: the client is playing the command's weapon sounds and impacts for
/// itself, so the server's copies must not reach it as well.
/// <c>CBasePlayer::PhysicsSimulate</c> sets the host around the command run
/// when <c>IsPredictingWeapons()</c>, which is the client's
/// <c>cl_predict</c> and <c>cl_predictweapons</c> userinfo, both non-zero
/// (player.cpp, ClientSettingsChanged). Here the arsenal sets it around the
/// weapon machine's run.
/// </para>
///
/// <para>
/// THE TWO RULES. A filter that asks for <see cref="UsePredictionRules"/>
/// (<c>CBaseCombatWeapon::WeaponSound</c>'s) loses the host and never takes
/// it back. A temp entity sent while there is a host loses it unless its
/// filter ignores the prediction cull, and is not sent at all when nobody is
/// left (<c>CTempEntsSystem::SuppressTE</c>, te.cpp). Everything else in the
/// window still reaches the host, as in stock.
/// </para>
/// </summary>
public sealed class PredictionSuppression
{
    private readonly IClientInfo _clients;

    public PredictionSuppression(IClientInfo clients) => _clients = clients;

    /// <summary>The suppressed player's index, or 0 when nobody is.</summary>
    public int Host { get; private set; }

    /// <summary>
    /// <c>CBasePlayer::IsPredictingWeapons</c>: the player's client predicts
    /// and predicts weapons. A value the client never sent reads as stock's
    /// <c>Q_atoi("")</c>, which is 0.
    /// </summary>
    public bool PredictsWeapons(int playerIndex)
        => playerIndex > 0
           && Atoi(_clients.ClientConVar(playerIndex, "cl_predict")) != 0
           && Atoi(_clients.ClientConVar(playerIndex, "cl_predictweapons")) != 0;

    /// <summary>
    /// <c>SuppressHostEvents( player )</c> until the scope is disposed, which
    /// puts back the host it replaced (stock's <c>SuppressHostEvents( NULL )</c>
    /// after the commands, made nesting-safe).
    /// </summary>
    public HostScope Suppress(int playerIndex)
    {
        var scope = new HostScope(this, Host);
        Host = playerIndex > 0 ? playerIndex : 0;
        return scope;
    }

    /// <summary><c>CRecipientFilter::UsePredictionRules</c>: the host leaves the filter for good.</summary>
    public void UsePredictionRules(RecipientFilter filter) => filter.UsePredictionRules(Host);

    /// <summary>
    /// <c>CTempEntsSystem::SuppressTE</c>: true when the temp entity must not
    /// be sent. Takes the host out of <paramref name="filter"/> first, unless
    /// the filter ignores the prediction cull.
    /// </summary>
    public bool SuppressTempEntity(RecipientFilter filter, bool ignorePredictionCull)
    {
        if (Host <= 0)
            return false;

        if (!ignorePredictionCull)
            filter.Remove(Host);

        return filter.Count == 0;
    }

    private static int Atoi(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        bool negative = i < text.Length && text[i] == '-';
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
            i++;

        int value = 0;
        for (; i < text.Length && text[i] is >= '0' and <= '9'; i++)
            value = unchecked(value * 10 + (text[i] - '0'));

        return negative ? -value : value;
    }

    /// <summary>The suppression window; disposing it restores the previous host.</summary>
    public readonly struct HostScope : IDisposable
    {
        private readonly PredictionSuppression? _owner;
        private readonly int _previous;

        internal HostScope(PredictionSuppression owner, int previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_owner is not null)
                _owner.Host = _previous;
        }
    }
}
