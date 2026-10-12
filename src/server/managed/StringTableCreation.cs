namespace SourceSharp.Abi;

/// <summary>
/// The server's half of <see cref="StringTableRegistry"/> (plan_abi_hotreload
/// Phase 1): creating a table is the server-only StringTableCreate slot, so the
/// create path lives in this server assembly while the registry's discovery,
/// binding and reading stay in SourceSharp.Shared, which both sides load. An
/// extension, so the server's <c>registry.CreateAll()</c> reads as it did.
/// </summary>
public static class StringTableCreation
{
    /// <summary>
    /// Creates every declared table. <b>Only valid from
    /// <c>IServerGameDLL::CreateNetworkStringTables</c></b>, which is the one
    /// moment the engine allows a table to be made; anywhere else the engine
    /// refuses and says so. Returns how many were created.
    ///
    /// <para>
    /// A refusal is reported per table and is <b>not</b> fatal. The failures the
    /// engine actually produces here are per-table — the 32-table cap, or a name
    /// something else already took — and taking a live server down at map load
    /// for one mod's table is a worse outcome than that table not working. What
    /// the mod is left with is the absent-table path, which refuses every
    /// operation and names the table: loud, and local to the feature that
    /// declared it.
    /// </para>
    /// </summary>
    public static int CreateAll(this StringTableRegistry registry)
    {
        int made = 0;

        foreach (var declaration in registry.Declarations)
        {
            if (registry.Tables.Create(declaration.Name, declaration.MaxEntries) >= 0)
            {
                made++;
                continue;
            }

            // Already warned on the native side with the table's name in it.
            // Said again here because the two readers are different people: the
            // console line names the engine's refusal, this one names the
            // DECLARATION whose table is missing.
            registry.Log.Warning(
                $"[SourceSharp] could not create the '{declaration.Name}' string table declared by "
              + $"{declaration.Contract.Name}; every operation on it will be refused this map\n");
        }

        registry.Log.Msg($"[SourceSharp] created {made} of {registry.Declarations.Count} declared string table(s)\n");
        return made;
    }
}
