//========= SourceSharp — matching a classname, without the engine ===========//
//
// The one piece of the standalone shell that is string arithmetic rather than
// engine plumbing, pulled out so it can be tested without a running Source.
//
// It is worth testing on its own because it exists to AGREE with something
// else, and a disagreement is invisible from either end: an iteration that
// walks both hierarchies has to apply one rule to both halves, and a query that
// answered for the CBaseEntity entities and not the others would under-report
// by exactly the classes this migration has moved — which reads as "those
// entities are gone" rather than as a bug in a comparison.
//
// No engine headers on purpose. Q_stricmp lives in tier1 and would drag the
// whole of it into a unit test; the comparison below is ASCII-only, which is
// all a classname has ever been.
//
// WHAT USED TO BE HERE, and why it is not (lane 3c-entityshell).
//
//   StandaloneOutputIndex -- which slot an output name occupies. 0 callers
//   anywhere: shell_standalone_base.cpp:2292 records that routing an output
//   through it "was asking C# a question C# had answered", and the arm that
//   asked went with that finding. Only its own gtest still named it.
//
//   StandaloneNameEquals -- case-insensitive ASCII equality. Its last caller
//   was the cs_sv_setlocal console command in shell_standalone_hierarchy.cpp,
//   which is managed now (SourceSharp.Server/HierarchyCommands.cs) and asks the
//   entity directory by name instead of sweeping every standalone entity.
//
// Both went with the code that called them and with the assertions that pinned
// them, in one commit, rather than being left as two tested functions the build
// does not reach. game/public/bullet_glass.h:39 still names StandaloneNameEquals
// in a comment explaining why it does NOT share it — that argument is about the
// stock include path and is unaffected by this header's contents.
//
//=============================================================================//
#ifndef SOURCESHARP_STANDALONE_NAMES_H
#define SOURCESHARP_STANDALONE_NAMES_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Whether a classname matches a query, with the SDK's own wildcard rule.
//
// This exists to agree with CGlobalEntityList::FindEntityByClassname rather
// than to be reasonable on its own, because an iteration that walks both
// hierarchies must apply ONE rule to both halves. A query that answered for the
// CBaseEntity entities and not the others would under-report by exactly the
// classes this migration has moved — which reads as "those entities are gone".
//
// NamesMatch in baseentity.cpp is the original, and its own comment says only a
// TRAILING star is supported. So "*" is everything, "prop_*" is a prefix, and a
// star anywhere else is a literal character.
//
// Case-insensitive, ASCII, inline: not strcasecmp, which is POSIX and spelled
// differently on the Windows this tree also builds for, and not Q_stricmp,
// which is tier1.
//-----------------------------------------------------------------------------
inline bool StandaloneNameMatches( const char *pQuery, const char *pName )
{
	if ( !pQuery || !pName )
		return false;

	// An unnamed thing matches only the empty query or the bare wildcard, which
	// is what NamesMatch answers for NULL_STRING.
	if ( !pName[0] )
		return !pQuery[0] || *pQuery == '*';

	while ( *pName && *pQuery )
	{
		char a = *pName;
		char b = *pQuery;

		if ( a >= 'A' && a <= 'Z' ) a = (char)( a - 'A' + 'a' );
		if ( b >= 'A' && b <= 'Z' ) b = (char)( b - 'A' + 'a' );

		if ( a != b )
			break;

		pName++;
		pQuery++;
	}

	if ( !*pQuery && !*pName )
		return true;

	return *pQuery == '*';
}

#endif // SOURCESHARP_STANDALONE_NAMES_H
