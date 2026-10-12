//========= SourceSharp — who asked the process to end =======================//
//
// The dev API's exit record (debug-api lane, item 5): a game that ended without
// the caller asking used to be classified from a log tail -- "the window was
// closed, `quit` was typed, or it was sent SIGTERM" -- because nothing recorded
// which. This is the native half, and it RECORDS FACTS ONLY:
//
//   - a signal: which one, and the sender's pid and uid (SA_SIGINFO), then the
//     handler that was there before is run exactly as it would have been;
//   - SDL's quit event and its window-close event, from an event watch;
//   - a fatal stop (SourceSharp_FatalReport), written straight to the record
//     file the managed side named, because managed code never runs again after.
//
// Which of these was "the" reason is decided by managed code
// (Debug.Api's ExitRecord), never here.
//
//=============================================================================//

#ifndef SOURCESHARP_EXIT_WATCH_H
#define SOURCESHARP_EXIT_WATCH_H
#pragma once

#include <stdint.h>

// What ExitWatch_Arm installed: bits of these.
enum
{
	kExitWatchSignals = 1,   // the SIGTERM / SIGINT / SIGHUP recorders
	kExitWatchSdl     = 2,   // the SDL event watch (no SDL in the process: absent)
};

// The facts, in order, as ExitWatch_Read writes them. Times are milliseconds
// since the first arm; a count of zero leaves its other fields zero.
enum ExitWatchFact_t
{
	kExitFactVersion = 0,      // 1
	kExitFactArmed,            // kExitWatch* bits
	kExitFactNowMs,            // the clock the times below are on, read now
	kExitFactSignalCount,
	kExitFactFirstSigno, kExitFactFirstPid, kExitFactFirstUid, kExitFactFirstAtMs,
	kExitFactLastSigno, kExitFactLastPid, kExitFactLastUid, kExitFactLastAtMs,
	kExitFactSdlQuitCount, kExitFactSdlQuitAtMs,
	kExitFactWindowCloseCount, kExitFactWindowCloseAtMs,
	// What each watched signal's disposition was before the arm: 0 default,
	// 1 ignored, 2 a plain handler, 3 an SA_SIGINFO handler.
	kExitFactPrevTerm, kExitFactPrevInt, kExitFactPrevHup,
	kExitFactCount
};

// Installs the recorders (once per process; a second arm only replaces the
// record path) and remembers pRecordPath for a fatal stop. Returns the
// kExitWatch* bits now installed.
int32_t ExitWatch_Arm( const char *pRecordPath );

// Writes up to nMax facts (ExitWatchFact_t order) into pOut; returns how many.
int32_t ExitWatch_Read( int32_t *pOut, int32_t nMax );

// The shutdown half: every recorder Arm installed is taken back out -- the
// signal handlers are restored and SDL's event watch is REMOVED, because the
// module holding OnSdlEvent is about to be unmapped and SDL would still call
// it (SIGTERM-exits-139: a jump into unmapped code on the last event pump).
// The facts already recorded are kept: the managed side writes its exit
// record after disarming. Returns the kExitWatch* bits that were installed
// (0 when nothing was armed).
int32_t ExitWatch_Disarm();

// The recorders themselves, exposed for the tests: what a signal and an SDL
// event do when they arrive.
void ExitWatch_OnSignalForTest( int nSigno, int nSenderPid, int nSenderUid );
void ExitWatch_OnSdlEventForTest( const void *pEvent );

// Restores every handler the arm replaced and forgets the facts. For the tests.
void ExitWatch_ResetForTest();

#endif // SOURCESHARP_EXIT_WATCH_H
