; The rules the generators report, listed so RS2008 stops asking.
;
; RS2008 is not pedantry here: it is the only thing that notices a rule id
; being added, changed or quietly dropped, and this project has already shipped
; diagnostics nobody had ever seen fire. A rule that vanishes from a generator
; leaves no trace anywhere else -- the code it used to refuse simply starts
; compiling. This file is the list that has to be edited on purpose.
;
; Everything is Unshipped because nothing here has a released version to be
; shipped against; the analyzers ship with the mod they check.
;
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SSMSG001 | SourceSharp | Error | Unsupported user message member type
SSMSG002 | SourceSharp | Error | A user message member needs a getter and a setter
SSMSG003 | SourceSharp | Error | A user message payload has more than one handler
SSMSG004 | SourceSharp | Error | A user message handler names more than one payload
SSMSG005 | SourceSharp | Error | A user message payload must be declared beside its handler
SSMSG006 | SourceSharp | Error | A user message payload must be a partial, non-generic struct or class
SSMSG007 | SourceSharp | Error | A user message array member must state its most
SSMSG008 | SourceSharp | Error | A user message payload cannot fit the engine's limit
SSMSG009 | SourceSharp | Error | A fixed user message size disagrees with its payload
SSMSG010 | SourceSharp | Error | A user message payload holds state that does not travel
SSNET001 | SourceSharp | Error | A net state must name its entity class
SSNET002 | SourceSharp | Error | Unsupported networked type
SSNET003 | SourceSharp | Error | A networked property needs a get and a set
SSNET004 | SourceSharp | Error | Unsupported networked array element
SSNET005 | SourceSharp | Error | A networked array must state its length
SSNET006 | SourceSharp | Error | A declared kind can only reinterpret an integer's sign
SSNET007 | SourceSharp | Error | The engine block was not found
SSVAR002 | SourceSharp | Error | Unsupported map key type
SSVAR003 | SourceSharp | Error | A map key needs a get and a set
SSVAR004 | SourceSharp | Error | Two properties claim one map key
SSVAR005 | SourceSharp | Error | An output's name is already taken by its base
SSVAR007 | SourceSharp | Error | Two var sets on one class claim one map key
SSVAR008 | SourceSharp | Error | A var set an entity holds without declaring
SSVAR009 | SourceSharp | Error | [ScriptVars] on something that declares no keys
SSVAR010 | SourceSharp | Warning | A var set shadows a map key its base declares
SSVAR011 | SourceSharp | Warning | A var set reuses a property name its base declares
SSVAR012 | SourceSharp | Error | A map key's enum must number every member explicitly
SSTAB001 | SourceSharp | Error | A string table declaration must extend IEngineStringTable
SSTAB002 | SourceSharp | Error | A string table must have room in it
SSTAB003 | SourceSharp | Error | A string table declaration declares no members of its own
SSTAB004 | SourceSharp | Error | A string table must be declared at namespace level
SSVT001 | SourceSharp | Error | A managed vtable's layout must be an [ImplementedByManaged] struct
SSVT002 | SourceSharp | Error | Every slot of the layout needs a contract method
SSVT003 | SourceSharp | Error | Every contract method needs a slot in the layout
SSVT004 | SourceSharp | Error | A contract method must take and return exactly what its slot does
SSVT005 | SourceSharp | Error | Release is the generator's slot
SSVT006 | SourceSharp | Error | A contract method name is a slot name, so it may not be overloaded
SSVT007 | SourceSharp | Error | A managed vtable contract must be a non-generic interface at namespace level
