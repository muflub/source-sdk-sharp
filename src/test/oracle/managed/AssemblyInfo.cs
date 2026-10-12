using Xunit;

// One thread, as in SourceSharp.Tests (its AssemblyInfo.cs gives the reason):
// the ports under test bind per-side engine state that two tests at once
// would share.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
