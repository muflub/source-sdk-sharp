//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// CTriggerMultiple (triggers.cpp): fires OnTrigger, naming whoever set it off,
/// on every frame something that passes is inside, but not again until its
/// wait is over. A wait of 0 is 0.2 s, as stock's Spawn makes it; a negative
/// one fires once and removes it.
/// </summary>
[ScriptedEntity(EntityNames.TriggerMultiple, ScriptedEntityKind.Brush)]
public sealed class TriggerMultiple : TriggerVolume
{
    public TriggerMultiple(IGameClock clock)
        : base(clock)
    {
    }

    /// <summary>The time its wait is over (stock's next think, MultiWaitOver).</summary>
    [SaveField] private float _waitOver;

    /// <summary>Fired with a negative wait: stock's SetTouch( NULL ) until it is gone.</summary>
    [SaveField] private bool _spent;

    /// <summary>MultiTouch runs on every frame of a touch, not only the first.</summary>
    public override bool TouchEveryTick => true;

    public override void OnSpawn()
    {
        base.OnSpawn();

        if (Vars.Wait == 0f)
            Vars.Wait = 0.2f;
    }

    protected override void Fire(EntityHandle toucher) { }

    /// <summary>MultiTouch: ActivateMultiTrigger, unless it is still waiting.</summary>
    protected override void Touching(EntityHandle toucher)
    {
        if (_spent || Clock.Now < _waitOver)
            return;

        OnTrigger.Fire(toucher);

        // A negative wait is trigger_once's: fired, and gone.
        if (Vars.Wait > 0f)
        {
            _waitOver = Clock.Now + Vars.Wait;
            return;
        }

        _spent = true;
        Remove();
    }
}
