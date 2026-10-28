// Records why the process ended: signals (with sender pid and uid), SDL quit and
// window-close events, and fatal stops. It records facts only; managed code
// decides which was the reason.

#pragma once

#include <stdint.h>

// What ExitWatch_Arm installed: bits of these.
enum
{
	kExitWatchSignals = 1, // the SIGTERM / SIGINT / SIGHUP recorders
	kExitWatchSdl = 2, // the SDL event watch (no SDL in the process: absent)
};

// The facts, in order, as ExitWatch_Read writes them. Times are milliseconds
// since the first arm; a count of zero leaves its other fields zero.
enum ExitWatchFact_t
{
	kExitFactVersion = 0, // 1
	kExitFactArmed, // kExitWatch* bits
	kExitFactNowMs, // the clock the times below are on, read now
	kExitFactSignalCount,
	kExitFactFirstSigno,
	kExitFactFirstPid,
	kExitFactFirstUid,
	kExitFactFirstAtMs,
	kExitFactLastSigno,
	kExitFactLastPid,
	kExitFactLastUid,
	kExitFactLastAtMs,
	kExitFactSdlQuitCount,
	kExitFactSdlQuitAtMs,
	kExitFactWindowCloseCount,
	kExitFactWindowCloseAtMs,
	// What each watched signal's disposition was before the arm: 0 default,
	// 1 ignored, 2 a plain handler, 3 an SA_SIGINFO handler.
	kExitFactPrevTerm,
	kExitFactPrevInt,
	kExitFactPrevHup,
	kExitFactCount
};

// Installs the recorders (once per process; a second arm only replaces the
// record path) and remembers pRecordPath for a fatal stop. Returns the
// kExitWatch* bits now installed.
int32_t ExitWatch_Arm( const char *pRecordPath );

// Writes up to nMax facts (ExitWatchFact_t order) into pOut; returns how many.
int32_t ExitWatch_Read( int32_t *pOut, int32_t nMax );

// Removes every recorder Arm installed. SDL's event watch must go before this
// module is unmapped, or SDL calls into unmapped code. Recorded facts are kept.
// Returns the kExitWatch* bits that were installed.
int32_t ExitWatch_Disarm();

// The recorders themselves, for the tests.
void ExitWatch_OnSignalForTest( int nSigno, int nSenderPid, int nSenderUid );
void ExitWatch_OnSdlEventForTest( const void *pEvent );

// Restores every handler the arm replaced and forgets the facts. For the tests.
void ExitWatch_ResetForTest();
