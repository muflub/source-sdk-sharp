using SourceSharp.MapGen;

// Two jobs, told apart by the first argument.
//
// "pass" is Part 6's Phase 4 gate — the one batch pass over the sandbox map,
// scoring what a running server actually has against what the map declares.
// tools/sandbox-pass collects the two files from debug-api and calls this; the
// judgement lives here, in managed code, where SandboxPassTests can reach it.
if (args.Length > 0 && args[0] == "pass")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: MapGen pass <mapents.json> <live.jsonl>");
        return 2;
    }

    var declared = SandboxPass.ReadMapEntities(File.ReadAllText(args[1]));
    var live = SandboxPass.ReadLiveCounts(File.ReadAllLines(args[2]));
    var shortfalls = SandboxPass.Audit(declared, live);

    // The leg that was missing: what the mod REGISTERS against what the map
    // CONTAINS. Without it the gate scores a map against itself, and a .bsp
    // seventy-two classnames behind the generator passes.
    var absences = SandboxPass.AuditCoverage(SandboxPass.Registered(), declared);

    // And the leg a classname check structurally cannot reach: after the temp
    // entity port one classname stands for forty-three effects, so coverage is
    // the `te` keyvalue on each ss_te_tester rather than the classname once
    // (plan 9.2).
    var temps = SandboxPass.AuditTempEntCoverage(SandboxPass.RegisteredTempEnts(), declared);

    foreach (string line in SandboxPass.Report(declared, live, shortfalls, absences, temps))
        Console.WriteLine(line);

    return shortfalls.Any(s => s.Fails) || absences.Any(a => a.Fails) || temps.Count > 0 ? 1 : 0;
}

// "mapcheck" is the coverage leg on its own, against a .bsp on disk and nothing
// else — no game, no debug-api, no map tools. It is what `make map` runs the moment
// a map finishes compiling, so a compile that came out short says so there and
// then rather than the next time somebody runs the gate.
//
// It lives here because this is where a .bsp definitely exists. It used to say
// it lived here because `make test` structurally COULD NOT answer the question
// — the .bsp needs Wine and a Steam depot to produce, and docs/sandbox.md keeps
// the suite free of that. That does not follow: building a map needs the
// toolchain, reading one needs BspEntities and a file already on disk. The gap
// it justified let ss_sandbox.bsp sit 72 classnames behind with `make test`
// green throughout. SandboxArtefactTests now asks in the suite too, and skips
// only in a tree that has never compiled a map.
if (args.Length > 0 && args[0] == "mapcheck")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: MapGen mapcheck <map.bsp>");
        return 2;
    }

    List<SandboxPass.DeclaredEntity> contents;

    try
    {
        contents = BspEntities.Read(args[1]);
    }
    catch (Exception error) when (error is IOException or InvalidDataException)
    {
        // Refused, not excused. A .bsp that cannot be read is not a .bsp that
        // contains everything.
        Console.Error.WriteLine($"FAIL — could not read {args[1]}: {error.Message}");
        return 2;
    }

    var missing = SandboxPass.AuditCoverage(SandboxPass.Registered(), contents);

    int classNames = contents.Select(e => e.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    Console.WriteLine($"{args[1]}: {contents.Count} entities across {classNames} classnames");
    Console.WriteLine($"the mod registers {SandboxPass.Registered().Count} classes");

    foreach (var absence in missing)
    {
        string verdict = absence.Fails ? "FAIL" : "ok  ";
        string reason = absence.Exemption ?? "the mod registers this and the map contains none";

        Console.WriteLine($"  {verdict} {absence.ClassName}: absent — {reason}");
    }

    // THE TEMP ENTITY LEG, and it is a separate question rather than more of the
    // same one. The rule above compares CLASSNAMES, and after this port ONE
    // classname — ss_te_tester — stands for forty-three effects: a single one
    // anywhere in the map satisfies it while forty-two go undemonstrated. So the
    // `te` keyvalue is read out of each tester in the entity lump instead
    // (plan 9.2). Printed even when it passes, because a leg that says nothing
    // when it passes and nothing when it never ran is the failure the classname
    // leg was added for.
    var temps = SandboxPass.RegisteredTempEnts();
    var absentTemps = SandboxPass.AuditTempEntCoverage(temps, contents);
    int testers = contents.Count(e =>
        string.Equals(e.ClassName, SandboxPass.TeTesterClass, StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"the mod sends {temps.Count} temp entities; the map holds {testers} "
                    + $"{SandboxPass.TeTesterClass} placement(s) covering "
                    + $"{temps.Count - absentTemps.Count} of them");

    foreach (var absence in absentTemps)
    {
        // No exemption column: firing IS the whole of what a temp entity does,
        // so there is no list an absence could be excused from (plan 9.2).
        Console.WriteLine($"  FAIL {absence.ClassName}: no ss_te_tester in the map fires it");
    }

    int unexcused = missing.Count(a => a.Fails) + absentTemps.Count;

    Console.WriteLine(unexcused == 0
        ? $"PASS — every class the mod registers has an instance in the map ({missing.Count} exempt by name), "
          + $"and every temp entity it sends has an {SandboxPass.TeTesterClass} that fires it"
        : $"FAIL — {unexcused} registered class(es) or temp entit(ies) absent from the map "
          + "with nothing to account for it");

    return unexcused == 0 ? 0 : 1;
}

// "mapdrift" is the staleness leg: the compiled .bsp on disk against the
// generator that is supposed to have produced it.
//
// mapcheck above asks whether the map CONTAINS every registered class, which is
// a coverage question and misses a map that has every class and the wrong
// keyvalues on them. That is the state ss_sandbox.bsp was in: complete, and
// carrying spawnflags 0 on the env_explosion the generator gives spawnflags 2,
// so the station's reset button deleted its own subject.
//
// The same judgement runs inside `make test` via SandboxPassTests, which is
// where it catches a stale map without anyone remembering to ask. This command
// is for asking on purpose, and for `make map` to fail loudly on the spot.
if (args.Length > 0 && args[0] == "mapdrift")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: MapGen mapdrift <map.bsp>");
        return 2;
    }

    List<SandboxPass.DeclaredEntity> compiled;

    try
    {
        compiled = BspEntities.Read(args[1]);
    }
    catch (Exception error) when (error is IOException or InvalidDataException)
    {
        Console.Error.WriteLine($"FAIL — could not read {args[1]}: {error.Message}");
        return 2;
    }

    var drifts = SandboxPass.Staleness(SandboxPass.FromGenerator(Sandbox.Build()), compiled);

    foreach (var drift in drifts)
        Console.WriteLine($"  FAIL {drift}");

    Console.WriteLine(drifts.Count == 0
        ? $"PASS — {args[1]} agrees with Sandbox.cs"
        : $"FAIL — {args[1]} is behind Sandbox.cs in {drifts.Count} place(s); recompile it with `make map MAP=ss_sandbox`");

    return drifts.Count == 0 ? 0 : 1;
}

// "town" writes tf2_descent_town (plan_host_sdk §2.10) to the path given: per-tree
// build output, compiled by `make map MAP=tf2_descent_town`, never committed.
if (args.Length > 0 && args[0] == "town")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: MapGen town <dest.vmf>");
        return 2;
    }

    var town = new TF2DescentTown().Build();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1], town.Write());
    Console.WriteLine($"{args[1]}");
    Console.WriteLine($"  {town.Entities.Count} entities, {town.WorldSolids.Count} world brushes");
    return 0;
}

// Where the .vmf goes. An argument wins, so a caller can put it somewhere else;
// otherwise it lands in maps/ beside the other map sources, which is what
// "make map" compiles from.
string destination = args.Length > 0
    ? args[0]
    : Path.Combine(FindRepoRoot(), "maps", "ss_sandbox.vmf");

var map = Sandbox.Build();

Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
File.WriteAllText(destination, map.Write());

Console.WriteLine($"{destination}");
Console.WriteLine($"  {Sandbox.Stations.Count} stations, {map.Entities.Count} entities, " +
                  $"{map.WorldSolids.Count} world brushes");

return 0;

// The generator is run from wherever, so the repo is found rather than assumed.
//
// This used to walk up looking for a Makefile and keep its own copy of that
// walk. It is the same search SandboxArtefact does, and the same search that
// let a nested worktree read the enclosing checkout's map, so there is now one
// implementation of "which tree am I in" and both callers use it. The
// difference that matters for a WRITER: anchoring on two markers instead of one
// means this cannot pick a directory under src/ that happens to hold a Makefile
// and write the .vmf into it.
static string FindRepoRoot() =>
    RepoTree.FindRoot(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
