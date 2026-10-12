; The rules SourceSharp.Analyzers reports, listed so RS2008 stops asking: it is
; the only thing that notices a rule id being added, changed or quietly dropped.
;
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SS0001 | SourceSharp | Error | Mutable static state
SS0002 | SourceSharp | Error | A static-state waiver names no mutable static
SS0003 | SourceSharp | Error | The static-state waiver file is missing or malformed
SS0010 | SourceSharp | Error | A mod declares a static field
SS0011 | SourceSharp | Error | A mod declares an event
SS0012 | SourceSharp | Error | A mod uses a static of the core or the process
SS0013 | SourceSharp | Error | A mod subscribes to an event
SS0020 | SourceSharp | Error | A test sets a process environment variable
SS0060 | SourceSharp | Error | A class in the serial collection has no row
SS0061 | SourceSharp | Error | A serial-collection row names a class not in the collection
SS0062 | SourceSharp | Error | A second collection definition
SS0030 | SourceSharp | Error | An interface assembly holds behaviour
SS0050 | SourceSharp | Error | A Run(Action<string, bool>)-shaped test
SS0051 | SourceSharp | Error | A public method on a test class is not a test
SS0070 | SourceSharp | Error | An ABI thunk does not count itself first
SS0071 | SourceSharp | Error | An ABI thunk counts another thunk's op
