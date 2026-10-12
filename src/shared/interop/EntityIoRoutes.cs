//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// Delivers one input to one entity. Answers whether the entity took it.
///
/// <para>
/// A DELEGATE rather than a call, because the directory does not know what
/// an input MEANS. Routing a name to a <c>[ScriptedEvent]</c> method, or to
/// <c>OnInput</c>, is the registry's table and its depth guard; what is
/// here is which entity, when, and in what order.
/// </para>
///
/// <para>
/// Top-level since P1 run 3 (it was nested in EntityDirectory): the shared
/// <see cref="EntityIoRoutes"/> carries one, and the directory is the server's.
/// </para>
/// </summary>
public delegate bool EntityInputDelivery(EntityHandle target, string input, string? value,
                                 float number, EntityHandle activator);

/// <summary>
/// THE I/O HALF'S ROUTE FROM THE ENGINE SURFACE TO THE SIDE'S DIRECTORY, and
/// it exists because a slot was deleted.
///
/// <para>
/// <c>IEngineEntities.FireInput</c> is an area interface: an entity asks the
/// area it was handed, and the real implementation was one line into the
/// <c>EntityFireInput</c> ABI slot. Step 2a deletes that slot, and the answer
/// now lives in a directory — an INSTANCE, one per side module
/// (<c>plan.md</c> §1a).
/// </para>
///
/// <para>
/// <b>AN INSTANCE, ONE PER ENGINE SIDE</b> (plan_abi_hotreload D7). It was a
/// static class; each <see cref="EngineSide"/> now owns one, its container
/// registers it, the registry binds into the one it is constructed with, and
/// the frame driver (<c>ServerGameDll</c>) is handed the same one. Two game
/// instances in one process no longer deliver into each other's directories.
/// </para>
///
/// <para>
/// Of the four routes the static held, two remain. <c>AddOutput</c> goes to
/// the entity's own <c>Owner</c> registry, which every registry-built entity
/// has; the SDK hierarchy's delivery is a property of the directory that asks
/// it (<c>EntityDirectory.SdkDelivery</c>).
/// </para>
/// </summary>
public sealed class EntityIoRoutes
{
    /// <summary>Delivers one input synchronously. Null when no registry has bound one.</summary>
    public EntityInputDelivery? Input { get; private set; }

    /// <summary>Binds the route. The registry's constructor is the one caller.</summary>
    public void Bind(EntityInputDelivery route) => Input = route;

    /// <summary>
    /// THE ONE ENTITY I/O SERVICE PASS — the sub-ioqueue lane.
    ///
    /// <para>
    /// It is a route rather than a call on the directory because the frame
    /// driver, <c>ServerGameDll.GameFrame</c>, is where stock services the
    /// queue from (<c>gameinterface.cpp:1383</c>) and the driver has no
    /// directory: the directory belongs to the registry. Same shape and same
    /// reason as <see cref="Input"/> above it.
    /// </para>
    ///
    /// <para>
    /// Answers how many events were delivered; zero with nothing bound.
    /// </para>
    /// </summary>
    public Func<float, int>? ServiceQueue { get; private set; }

    /// <inheritdoc cref="ServiceQueue"/>
    /// <remarks>
    /// TAKES NULL, deliberately: a test that borrows the route has to be able
    /// to put back the nothing it found, and the only alternative on offer is
    /// <see cref="Unbind"/>, which would clear <see cref="Input"/> with it.
    /// </remarks>
    public void BindServiceQueue(Func<float, int>? route) => ServiceQueue = route;

    /// <inheritdoc cref="ServiceQueue"/>
    public int Service(float curTime)
        => ServiceQueue is null ? 0 : ServiceQueue(curTime);

    /// <summary>Drops both routes.</summary>
    public void Unbind()
    {
        Input = null;
        ServiceQueue = null;
    }

    /// <summary>
    /// Drops the routes if they belong to a side that is unloading, and answers
    /// how many went. <inheritdoc cref="ShellLessRoutes.Forget" path="/remarks/para[1]"/>
    /// </summary>
    internal int Forget(AssemblyLoadContext going)
    {
        int dropped = 0;

        if (ManagedCaches.CameFrom(Input, going))
        {
            Input = null;
            dropped++;
        }

        if (ManagedCaches.CameFrom(ServiceQueue, going))
        {
            ServiceQueue = null;
            dropped++;
        }

        return dropped;
    }

    /// <summary>
    /// Fires an input at one entity and answers whether it took it.
    ///
    /// <para>
    /// The NUMBER is parsed here, once, for the same reason
    /// <c>OnManagedInput</c> parsed it: a map sends "3" as a string, and a
    /// counter told to add three that reads the numeric form of a string gets
    /// zero and says so to nobody.
    /// </para>
    /// </summary>
    public bool FireInput(EntityHandle target, string input, string? value,
                          EntityHandle activator)
    {
        if (Input is null || string.IsNullOrEmpty(input))
            return false;

        string text = value ?? string.Empty;

        return Input(target, input, text, EventAction.ParseFloat(text), activator);
    }
}
