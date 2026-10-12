using Xunit;

// PARALLEL, EXCEPT WHAT IS SHARED. Test classes run beside each other; the ones
// that share something process-wide are in ONE serial collection, ProcessWide
// (ProcessWide.cs), which lists each member with what it shares and runs after
// every parallel class has finished.
//
// What they share is a property of what is under test, not flakiness: the
// engine binding is per SIDE and selected by a thread-local, several registries
// and routes are process-wide, and some suites publish state that others read
// back. Two such tests at once would be two tests sharing one engine, and the
// failure would look like a bug in whichever lost the race. So a class that
// needs one of those joins ProcessWide with a reason, or better, is handed its
// own instance of the thing instead.
//
// MaxParallelThreads: measured on the 32-core development box, see the commit
// that set it.
[assembly: CollectionBehavior(DisableTestParallelization = false, MaxParallelThreads = 16)]

