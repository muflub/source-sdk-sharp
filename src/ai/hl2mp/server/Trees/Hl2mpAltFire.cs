namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>Why a bot takes its second trigger now (§5.4, ruling 2026-11-03: "Bots should use alt fire").</summary>
public enum AltFireReason : byte
{
    None = 0,

    /// <summary>Two or more in one blast.</summary>
    Group,

    /// <summary>Someone who has not moved: a grenade flushes a camper.</summary>
    Camper,

    /// <summary>Someone who shows only part of themselves over or round something.</summary>
    Cover,

    /// <summary>Someone just gone round a corner, down a line the bot has looked along.</summary>
    Corner,

    /// <summary>Someone coming or going along the line: a ball down a corridor has only to fly straight.</summary>
    DownTheLine,

    /// <summary>Someone so near both barrels all land.</summary>
    Close,

    /// <summary>A frag, nearer than a throw wants: along the floor.</summary>
    Roll,

    /// <summary>A frag, nearer than a throw wants, on another level: underhand.</summary>
    Lob,

    /// <summary>A frag left behind on the way out.</summary>
    Retreat,

    /// <summary>A frag at someone above or below, showing too little for a gun.</summary>
    Height,
}

/// <summary>Which way a frag leaves: the geometry's choice (§5.4).</summary>
public enum FragHow : byte
{
    Throw = 0,
    Roll,
    Lob,
}

/// <summary>What a decide knows that the frag rule reads: only what the bot can see or believes.</summary>
/// <param name="Sighted">Its target is in sight.</param>
/// <param name="Distance">How far off the place is.</param>
/// <param name="HeightOff">How far above the bot the place's floor is.</param>
/// <param name="OthersNear">How many others it sees inside the blast of its target.</param>
/// <param name="StillSeconds">How long its target has stood.</param>
/// <param name="VisibleFraction">How much of its target shows.</param>
/// <param name="BehindCorner">Out of sight, and it believes where (the prefire's rule).</param>
/// <param name="CamperSpot">Out of sight, and the place is the target's habit.</param>
/// <param name="Escaping">The bot is on its way out of this fight.</param>
/// <param name="LoadedGun">The gun in its hands has rounds in it.</param>
/// <param name="FriendNear">A friend inside the blast of where it would land: none in free-for-all, a team game's rule.</param>
public readonly record struct FragSituation(bool Sighted, float Distance, float HeightOff, int OthersNear, float StillSeconds, float VisibleFraction,
                                            bool BehindCorner, bool CamperSpot, bool Escaping, bool LoadedGun, bool FriendNear);

/// <summary>What the inspector calls a reason: no string is made in a decide.</summary>
public static class AltFireReasons
{
    public static string Name(AltFireReason why) => why switch
    {
        AltFireReason.Group => "group",
        AltFireReason.Camper => "camper",
        AltFireReason.Cover => "cover",
        AltFireReason.Corner => "corner",
        AltFireReason.DownTheLine => "down the line",
        AltFireReason.Close => "close",
        AltFireReason.Roll => "roll",
        AltFireReason.Lob => "lob",
        AltFireReason.Retreat => "retreat",
        AltFireReason.Height => "height",
        _ => "",
    };
}

/// <summary>What a decide knows that a second trigger's rule reads: only what the bot can see.</summary>
/// <param name="Sighted">Its target is in sight.</param>
/// <param name="Distance">How far off its target is, or the corner.</param>
/// <param name="HeightOff">How far above the bot its target's feet are.</param>
/// <param name="ClosingSpeed">How fast its target comes at it, units/s; away is negative.</param>
/// <param name="AcrossSpeed">How fast its target crosses the line to it, units/s.</param>
/// <param name="StillSeconds">How long its target has stood.</param>
/// <param name="OthersNear">How many others it sees inside the blast of its target.</param>
/// <param name="VisibleFraction">How much of its target shows.</param>
/// <param name="CornerClear">No one in sight, and the line to the corner it fires at is known clear.</param>
/// <param name="FriendNear">A friend inside the blast of where it would land, or between: none in free-for-all, a team game's rule.</param>
public readonly record struct AltFireSituation(bool Sighted, float Distance, float HeightOff, float ClosingSpeed, float AcrossSpeed, float StillSeconds,
                                               int OthersNear, float VisibleFraction, bool CornerClear, bool FriendNear);

/// <summary>
/// When each weapon's second trigger is worth its cost (§5.4), pure over what the decide knows. The
/// cost a blast has is the bot's own: nothing lands inside its radius of the bot, allowing for
/// someone coming on while it flies, and that is what "not at its feet" comes to.
/// </summary>
public static class Hl2mpAltFire
{
    /// <summary>How far past its radius a blast must land from the bot: a body is wide, and a grenade can land short.</summary>
    public const float SafeShare = 1.15f;

    /// <summary>A grenade is lobbed no further than this: past it nobody can say where it lands.</summary>
    public const float LobReach = 1000f;

    /// <summary>The share of a blast's radius that catches a second body for real.</summary>
    public const float GroupShare = 0.6f;

    /// <summary>A body standing moves under this, for this long, to be a camper.</summary>
    public const float StillSpeed = 30f, CamperSeconds = 1.5f;

    /// <summary>Showing no more than this, of the five points sight samples, a body is behind something.</summary>
    public const float CoverFraction = 0.4f;

    /// <summary>
    /// A ball goes at nothing nearer than this, the distance within which someone in plain sight comes first: its
    /// charge is about 0.6 s with no gun in hand (WeaponLogic). Nor further than this; and only at someone crossing slower than this.
    /// </summary>
    public const float BallNearest = 400f, BallReach = 1500f, BallAcrossSpeed = 90f;

    /// <summary>A ball kills what it touches and flies on: it is pressed this share as readily as anything else.</summary>
    public const float BallChanceShare = 0.5f;

    /// <summary>Both barrels are for up close: the spread is ten degrees across.</summary>
    public const float DoubleReach = 192f;

    /// <summary>Past this a frag is thrown overhand; a roll wants the floor it stands on.</summary>
    public const float FragThrowFrom = 450f, RollFloor = 16f;

    /// <summary>
    /// A frag goes no further than this, and no nearer than <see cref="EscapeNearest"/> leaving a fight: its fuse
    /// is 2.5 s from the throw whatever the hold (stock's GRENADE_TIMER: no cooking), and by then a bot running is 475 u off.
    /// </summary>
    public const float FragReach = 900f, EscapeNearest = 150f;

    /// <summary>Showing no more than this over this rise, someone is out of a gun's easy reach.</summary>
    public const float HeightRise = 96f, HeightFraction = 0.6f;

    /// <summary>
    /// Why a bot holding a gun would switch to a frag now; <see cref="AltFireReason.None"/> if not (§5.4). A
    /// gunfight with a loaded gun and a clear shot goes on being one.
    /// </summary>
    public static AltFireReason FragReasonFor(in AltFireFacts frag, in FragSituation s)
    {
        float nearest = s.Escaping ? EscapeNearest : frag.BlastRadius * SafeShare;
        if (s.FriendNear || s.Distance < nearest || s.Distance > FragReach)
            return AltFireReason.None;
        if (!s.Sighted)
            return s.BehindCorner ? AltFireReason.Corner : s.CamperSpot ? AltFireReason.Camper : AltFireReason.None;
        if (s.Escaping)
            return AltFireReason.Retreat;
        if (s.OthersNear > 0)
            return AltFireReason.Group;
        if (s.VisibleFraction <= HeightFraction)
        {
            if (MathF.Abs(s.HeightOff) >= HeightRise)
                return AltFireReason.Height;
            if (s.StillSeconds >= CamperSeconds)
                return AltFireReason.Camper;
        }

        return AltFireReason.None;
    }

    /// <summary>Throw past <see cref="FragThrowFrom"/>; nearer, along its own floor or underhand to another.</summary>
    public static FragHow HowFor(float distance, float heightOff)
        => distance >= FragThrowFrom ? FragHow.Throw : MathF.Abs(heightOff) <= RollFloor ? FragHow.Roll : FragHow.Lob;

    /// <summary>Whether a blast of <paramref name="alt"/> at the target leaves the bot outside it, by the time it lands.</summary>
    public static bool SafeForSelf(in AltFireFacts alt, in AltFireSituation s)
    {
        float flight = alt.Speed > 0f ? s.Distance / alt.Speed : 0f;
        return s.Distance - MathF.Max(0f, s.ClosingSpeed) * flight >= alt.BlastRadius * SafeShare;
    }

    /// <summary>Why the second trigger is worth pressing now; <see cref="AltFireReason.None"/> if it is not.</summary>
    public static AltFireReason ReasonFor(in AltFireFacts alt, in AltFireSituation s) => alt.Kind switch
    {
        AltFireKind.Lob => LobReason(in alt, in s),
        AltFireKind.Ball => s.Sighted && s.Distance is >= BallNearest and <= BallReach && s.AcrossSpeed <= BallAcrossSpeed && !s.FriendNear
            ? AltFireReason.DownTheLine : AltFireReason.None,
        AltFireKind.Double => s.Sighted && s.Distance <= DoubleReach ? AltFireReason.Close : AltFireReason.None,
        AltFireKind.Underhand => FragReason(in alt, in s),
        _ => AltFireReason.None,
    };

    private static AltFireReason LobReason(in AltFireFacts alt, in AltFireSituation s)
    {
        if (s.FriendNear || s.Distance > LobReach || !SafeForSelf(in alt, in s))
            return AltFireReason.None;
        if (!s.Sighted)
            return s.CornerClear ? AltFireReason.Corner : AltFireReason.None;
        if (s.OthersNear > 0)
            return AltFireReason.Group;
        if (s.VisibleFraction <= CoverFraction)
            return AltFireReason.Cover;
        return s.StillSeconds >= CamperSeconds ? AltFireReason.Camper : AltFireReason.None;
    }

    private static AltFireReason FragReason(in AltFireFacts alt, in AltFireSituation s)
    {
        if (!s.Sighted || s.FriendNear || s.Distance >= FragThrowFrom || !SafeForSelf(in alt, in s))
            return AltFireReason.None;
        return MathF.Abs(s.HeightOff) <= RollFloor ? AltFireReason.Roll : AltFireReason.Lob;
    }
}
