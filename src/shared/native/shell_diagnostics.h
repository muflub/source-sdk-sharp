//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell diagnostics ===================================//
//
// The mod supplies only thin shells, so the interesting question during bring-up
// is always "what is the engine asking for that we do not answer?".
//
// The tools for that:
//   SOURCESHARP_STUB()          — mark a virtual that does nothing yet
//   SOURCESHARP_TRACE()         — log a call on a path the engine drives rarely
//   SOURCESHARP_TRACE_HOT()     — the same, on a path it drives every frame
//   SOURCESHARP_LINK_ENTITY()   — LINK_ENTITY_TO_CLASS that logs every creation
//   SourceSharp_DumpExportedInterfaces() — list everything the DLL exports
//
// Verbosity is the cl_/sv_sourcesharp_verbose ConVar, and it does NOT mean the
// same thing to every macro here. This is the sentence to get right, because
// the version of it that used to be here — "1 logs each stub once, 2 logs every
// call" — is true of SOURCESHARP_STUB and of nothing else, and reading it is
// what makes the trace's cost invisible:
//
//   0   errors only
//   1   the shipped default. Each STUB once. Every TRACE, EVERY TIME.
//   2   every STUB call, and the TRACE_HOT sites as well
//
// So a bare SOURCESHARP_TRACE is a line in the log of every ordinary player's
// session, once per call, and choosing between it and TRACE_HOT is a decision
// about that log rather than a stylistic one. See TRACE_HOT below for what one
// wrong choice cost.
//
//=============================================================================//
#pragma once

int  SourceSharp_GetVerbosity();

void SourceSharp_LogStub( const char *pWhat, bool *pbAlreadyLogged );
void SourceSharp_Trace( const char *pWhat, const char *pDetail );
void SourceSharp_DumpExportedInterfaces();
void SourceSharp_MarkDiagnosticsReady();


//-----------------------------------------------------------------------------
// WHAT THIS BUILD DOES NOT SHIP -- plan_entity.md step 7g.
//
// Call this where a still-compiled path's ONLY action was something a step
// deleted on "not shipped" grounds. It warns once per name with the reason and
// counts every reach; `cs_cl_notshipped` / `cs_sv_notshipped` print the ledger.
// The rule it serves is §7's: a behaviour that is refused says so, and never
// becomes a silent no-op.
//
// Both strings must OUTLIVE the call -- they are stored, not copied. String
// literals at the call site, which is what every caller passes.
//-----------------------------------------------------------------------------
class CShellNotShipped;
void SourceSharp_NotShipped( const char *pCapability, const char *pReason );

// SourceSharp_NotShippedLedger is NOT declared here (plan_shim.md §5 3e). Its
// one caller is the cs_{sv,cl}_notshipped ConCommand body in the same .cpp, so
// it is file-local; a header declaration would be a promise to the thirty-four
// translation units that include this file, and it would export
// CShellNotShipped's whole interface to every one of them.

// CLIENT ONLY. False always -- the HLTV camera is not shipped (7g) -- and it
// records the refusal by name when engine->IsHLTV() says the answer mattered.
// Written at the call sites that used to read `engine->IsHLTV()` to decide
// whether to drive C_HLTVCamera.
bool SourceSharp_HltvCameraAvailable();

void SourceSharp_LogEntityCreateFailed( const char *pClassName );
void SourceSharp_LogEntityDestroy( const char *pClassName );

// Logs every ServerClass / ClientClass this DLL registered, with its datatable.
void SourceSharp_DumpNetworkClasses();

void SourceSharp_LogEntityCreate( const char *pClassName );

//-----------------------------------------------------------------------------
// Marks a virtual that the engine calls but SourceSharp does not implement yet.
// Logs on first call (verbosity 1) or every call (verbosity 2).
//-----------------------------------------------------------------------------
#define SOURCESHARP_STUB()                                            \
	do {                                                              \
		static bool s_bLogged = false;                                \
		SourceSharp_LogStub( __FUNCTION__, &s_bLogged );              \
	} while ( 0 )

//-----------------------------------------------------------------------------
// Traces EVERY call, not just stubs. Use on shell functions that do real work,
// so the console shows the exact order the engine drives them in.
//-----------------------------------------------------------------------------
#define SOURCESHARP_TRACE()               SourceSharp_Trace( __FUNCTION__, NULL )
#define SOURCESHARP_TRACE_MSG( detail )   SourceSharp_Trace( __FUNCTION__, detail )

//-----------------------------------------------------------------------------
// For anything called every tick or every frame.
//
// A trace on a per-tick path is not a trace, it is a denial of service against
// the log. Those calls are still worth seeing when the question is "is this
// being called at all", so they are not removed — they need asking for.
//
// Use this rather than a bare SOURCESHARP_TRACE anywhere the caller is the
// engine's own loop.
//
// THE MEASUREMENT, so the rule is not argued from a hypothetical. This comment
// used to name PlayerMove and FAllowFlashlight as the worked example; neither
// function exists in this tree any more, and the rule spent that whole time
// resting on evidence nobody could check. What it rests on now:
// SourceSharp_TraceLine carried a bare SOURCESHARP_TRACE and Phase A11's census
// reads it at 1.573 crossings per server frame. A 2.5-minute dm_lockdown
// session wrote 33,134 console lines, 30,166 of them traces and 26,015 of those
// — 78 % of the whole log — about that one function. Demoting it and the two
// break-piece slots took the SAME scripted session to 3,728 lines, of which 765
// are traces: an 88.7 % cut, with every startup, precache, level-init, client-
// connect and level-shutdown line still in it. That is the cost of getting this
// choice wrong at ONE site, and it is why the macro exists.
//
// HotTraceAuditTests pins the rule, because a bare trace put back here compiles
// and passes everything else.
//-----------------------------------------------------------------------------
#define SOURCESHARP_TRACE_HOT()                                       \
	do {                                                              \
		if ( SourceSharp_GetVerbosity() > 1 )                         \
			SourceSharp_Trace( __FUNCTION__, NULL );                  \
	} while ( 0 )

#define SOURCESHARP_STUB_NAMED( name )                                \
	do {                                                              \
		static bool s_bLogged = false;                                \
		SourceSharp_LogStub( name, &s_bLogged );                      \
	} while ( 0 )


// Makes assertions log and continue instead of halting the game. Opt-in through
// -assertlog; see the implementation for why it is not the default.
void SourceSharp_InstallAssertLogging();

// -assertstack, which makes every FAILED assert print a native backtrace so a
// leaf assert names its caller rather than only itself, is reached THROUGH
// SourceSharp_InstallAssertLogging and is not declared here (plan_shim.md §5
// 3e). It is independent of -assertlog as a switch -- the two answer different
// questions, "did it fire" against "who fired it", and a hunt usually wants
// both -- but there has never been a caller that installs one without the
// other, which is why the installer is file-local.
//
// SourceSharp_GetAssertsLogged went at the same step: the count of assertions
// -assertlog demoted had no caller. The counter itself stays and the summary
// line prints it.
