//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier1/utlvector.h"

// SINGLE_INHERITANCE restricts the size of CBaseEntity pointers-to-member-functions to 4 bytes
class SINGLE_INHERITANCE CBaseEntity;
struct inputdata_t;

// Special value not rebased on save/load

typedef enum _fieldtypes
{
	FIELD_VOID = 0, // No type or value
	FIELD_FLOAT, // Any floating point value
	FIELD_STRING, // A string ID (return from ALLOC_STRING)
	FIELD_VECTOR, // Any vector, QAngle, or AngularImpulse
	FIELD_QUATERNION, // A quaternion
	FIELD_INTEGER, // Any integer or enum
	FIELD_BOOLEAN, // boolean, implemented as an int, I may use this as a hint for compression
	FIELD_SHORT, // 2 byte integer
	FIELD_CHARACTER, // a byte
	FIELD_COLOR32, // 8-bit per channel r,g,b,a (32bit color)
	FIELD_EMBEDDED, // an embedded object with a datadesc, recursively traverse and embedded class/structure based on an additional typedescription
	FIELD_CUSTOM, // special type that contains function pointers to it's read/write/parse functions

	FIELD_CLASSPTR, // CBaseEntity *
	FIELD_EHANDLE, // Entity handle
	FIELD_EDICT, // edict_t *

	FIELD_POSITION_VECTOR, // A world coordinate (these are fixed up across level transitions automagically)
	FIELD_TIME, // a floating point time (these are fixed up automatically too!)
	FIELD_TICK, // an integer tick count( fixed up similarly to time)
	FIELD_MODELNAME, // Engine string that is a model name (needs precache)
	FIELD_SOUNDNAME, // Engine string that is a sound name (needs precache)

	FIELD_INPUT, // a list of inputed data fields (all derived from CMultiInputVar)
	FIELD_FUNCTION, // A class function pointer (Think, Use, etc)

	FIELD_VMATRIX, // a vmatrix (output coords are NOT worldspace)

	// NOTE: Use float arrays for local transformations that don't need to be fixed up.
	FIELD_VMATRIX_WORLDSPACE, // A VMatrix that maps some local space to world space (translation is fixed up on level transitions)
	FIELD_MATRIX3X4_WORLDSPACE, // matrix3x4_t that maps some local space to world space (translation is fixed up on level transitions)

	FIELD_INTERVAL, // a start and range floating point interval ( e.g., 3.2->3.6 == 3.2 and 0.4 )
	FIELD_MODELINDEX, // a model index
	FIELD_MATERIALINDEX, // a material index (using the material precache string table)

	FIELD_VECTOR2D, // 2 floats

	FIELD_TYPECOUNT, // MUST BE LAST
} fieldtype_t;

// Field sizes...
template <int FIELD_TYPE>
class CDatamapFieldSizeDeducer
{
public:
	enum
	{
		SIZE = 0
	};

	static int FieldSize() { return 0; }
};

#define DECLARE_FIELD_SIZE( _fieldType, _fieldSize )  \
	template <>                                       \
	class CDatamapFieldSizeDeducer<_fieldType>        \
	{                                                 \
	public:                                           \
		enum                                          \
		{                                             \
			SIZE = _fieldSize                         \
		};                                            \
		static int FieldSize() { return _fieldSize; } \
	};

DECLARE_FIELD_SIZE( FIELD_FLOAT, sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_STRING, sizeof( intp ) )
DECLARE_FIELD_SIZE( FIELD_VECTOR, 3 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_VECTOR2D, 2 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_QUATERNION, 4 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_INTEGER, sizeof( int ) )
DECLARE_FIELD_SIZE( FIELD_BOOLEAN, sizeof( char ) )
DECLARE_FIELD_SIZE( FIELD_SHORT, sizeof( short ) )
DECLARE_FIELD_SIZE( FIELD_CHARACTER, sizeof( char ) )
DECLARE_FIELD_SIZE( FIELD_COLOR32, sizeof( int ) )
DECLARE_FIELD_SIZE( FIELD_CLASSPTR, sizeof( intp ) )
DECLARE_FIELD_SIZE( FIELD_EHANDLE, sizeof( int ) )
DECLARE_FIELD_SIZE( FIELD_EDICT, sizeof( int ) )
DECLARE_FIELD_SIZE( FIELD_POSITION_VECTOR, 3 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_TIME, sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_TICK, sizeof( int ) )
DECLARE_FIELD_SIZE( FIELD_MODELNAME, sizeof( intp ) )
DECLARE_FIELD_SIZE( FIELD_SOUNDNAME, sizeof( intp ) )
DECLARE_FIELD_SIZE( FIELD_INPUT, sizeof( int ) )
#ifdef POSIX
// pointer to members under gnuc are 8bytes if you have a virtual func
DECLARE_FIELD_SIZE( FIELD_FUNCTION, sizeof( uint64 ) )
#else
DECLARE_FIELD_SIZE( FIELD_FUNCTION, sizeof( int * ) )
#endif
DECLARE_FIELD_SIZE( FIELD_VMATRIX, 16 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_VMATRIX_WORLDSPACE, 16 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_MATRIX3X4_WORLDSPACE, 12 * sizeof( float ) )
DECLARE_FIELD_SIZE( FIELD_INTERVAL, 2 * sizeof( float ) ) // NOTE:  Must match interval.h definition
DECLARE_FIELD_SIZE( FIELD_MODELINDEX, sizeof( int ) )
DECLARE_FIELD_SIZE( FIELD_MATERIALINDEX, sizeof( int ) )

#define SIZE_OF_ARRAY( p ) _ARRAYSIZE( p )

// Normal offset of is invalid on non-array-types, this is dubious as hell. The rest of the codebase converted to the
// legit offsetof from the C headers, so we'll use the old impl here to avoid exposing temptation to others
#define _hacky_datamap_offsetof( s, m ) ( (size_t)&( ( (s *)0 )->m ) )

#define _FIELD( name, fieldtype, count, flags, mapname, tolerance )  \
	{ fieldtype,                                                     \
	  #name,                                                         \
	  { (int)_hacky_datamap_offsetof( classNameTypedef, name ), 0 }, \
	  count,                                                         \
	  flags,                                                         \
	  mapname,                                                       \
	  NULL,                                                          \
	  NULL,                                                          \
	  NULL,                                                          \
	  sizeof( ( (classNameTypedef *)0 )->name ),                     \
	  NULL,                                                          \
	  0,                                                             \
	  tolerance }

#define DEFINE_FIELD( name, fieldtype ) _FIELD( name, fieldtype, 1, FTYPEDESC_SAVE, NULL, 0 )

#define DEFINE_ARRAY( name, fieldtype, count ) _FIELD( name, fieldtype, count, FTYPEDESC_SAVE, NULL, 0 )

// Used by byteswap datadescs

// Extensions to datamap.h macros for predicted entities only

// Predictable macros, which include a tolerance for floating point values...

//#define DEFINE_DATA( name, fieldextname, flags ) _FIELD(name, fieldtype, 1,  flags, extname )

// INPUTS

// OUTPUTS
// the variable 'name' MUST BE derived from CBaseOutput
// we know the output type from the variable itself, so it doesn't need to be specified here
class ISaveRestoreOps;

// This field is masked for global entity save/restore
#define FTYPEDESC_SAVE 0x0002 // This field is saved to disk
// This field can be requested and written to by string name at load time
// This field can be written to by string name at run time, and a function called
// This field propogates it's value to all targets whenever it changes
// This is a table entry for a member function pointer
// This field is a pointer, not an embedded object
#define FTYPEDESC_OVERRIDE \
	0x0080 // The field is an override for one in a base class (only used by prediction system for now)

// Flags used by other systems (e.g., prediction system)
// This field is present in a network SendTable
// The field is local to the client or server only (not referenced by prediction code and not replicated by networking)
// The field is part of the prediction typedescription, but doesn't get compared when checking for errors

// The field is a model index (used for debugging output)

// The field is an index into file data, used for byteswapping.

// These flags apply to C_BasePlayer derived objects only
#define FTYPEDESC_VIEW_OTHER_PLAYER 0x2000 // By default you can only view fields on the local player (yourself),
//   but if this is set, then we allow you to see fields on other players
// Only show this data if the player is on the same team as the local player
// Never show this field to anyone, even the local player (unusual)

// Define shared with others, this is pred only, and FTYPEDESC_VIEW_OTHER_PLAYER is not used for pred.
// misyl: Only throw a prediction error if this changes from > 0 -> <= 0

// This is a FIELD_FLOAT and should only be checked to be within 0.001 of the networked info

struct typedescription_t;

class ISaveRestoreOps;

// Function prototype for all input handlers.
typedef void ( CBaseEntity::*inputfunc_t )( inputdata_t &data );

struct datamap_t;
struct typedescription_t;

enum
{
	TD_OFFSET_NORMAL = 0,
	TD_OFFSET_PACKED = 1,

	// Must be last
	TD_OFFSET_COUNT,
};

struct typedescription_t
{
	fieldtype_t fieldType;
	const char *fieldName;
	int fieldOffset[TD_OFFSET_COUNT]; // 0 == normal, 1 == packed offset
	unsigned short fieldSize;
	short flags;
	// the name of the variable in the map/fgd data, or the name of the action
	const char *externalName;
	// pointer to the function set for save/restoring of custom data types
	ISaveRestoreOps *pSaveRestoreOps;
	// for associating function with string names
	inputfunc_t inputFunc;
	// For embedding additional datatables inside this one
	datamap_t *td;

	// Stores the actual member variable size in bytes
	int fieldSizeInBytes;

	// FTYPEDESC_OVERRIDE point to first baseclass instance if chains_validated has occurred
	struct typedescription_t *override_field;

	// Used to track exclusion of baseclass fields
	int override_count;

	// Tolerance for field errors for float fields
	float fieldTolerance;
};

// Purpose: stores the list of objects in the hierarchy
//			used to iterate through an object's data descriptions
struct datamap_t
{
	typedescription_t *dataDesc;
	int dataNumFields;
	char const *dataClassName;
	datamap_t *baseMap;

	bool chains_validated;
	// Have the "packed" offsets been computed
	bool packed_offsets_computed;
	int packed_size;

#if defined( _DEBUG )
	bool bValidityChecked;
#endif // _DEBUG
};

// Macros used to implement datadescs
#define DECLARE_SIMPLE_DATADESC()                    \
	static datamap_t m_DataMap;                      \
	static datamap_t *GetBaseMap();                  \
	template <typename T>                            \
	friend void DataMapAccess( T *, datamap_t **p ); \
	template <typename T>                            \
	friend datamap_t *DataMapInit( T * );

#define BEGIN_DATADESC( className )                              \
	datamap_t className::m_DataMap = { 0, 0, #className, NULL }; \
	datamap_t *className::GetDataDescMap()                       \
	{                                                            \
		return &m_DataMap;                                       \
	}                                                            \
	datamap_t *className::GetBaseMap()                           \
	{                                                            \
		datamap_t *pResult;                                      \
		DataMapAccess( (BaseClass *)NULL, &pResult );            \
		return pResult;                                          \
	}                                                            \
	BEGIN_DATADESC_GUTS( className )

#define BEGIN_SIMPLE_DATADESC( className )                       \
	datamap_t className::m_DataMap = { 0, 0, #className, NULL }; \
	datamap_t *className::GetBaseMap()                           \
	{                                                            \
		return NULL;                                             \
	}                                                            \
	BEGIN_DATADESC_GUTS( className )

#define BEGIN_SIMPLE_DATADESC_( className, BaseClass )           \
	datamap_t className::m_DataMap = { 0, 0, #className, NULL }; \
	datamap_t *className::GetBaseMap()                           \
	{                                                            \
		datamap_t *pResult;                                      \
		DataMapAccess( (BaseClass *)NULL, &pResult );            \
		return pResult;                                          \
	}                                                            \
	BEGIN_DATADESC_GUTS( className )

#define BEGIN_DATADESC_GUTS( className )                                                                 \
	template <typename T>                                                                                \
	datamap_t *DataMapInit( T * );                                                                       \
	template <>                                                                                          \
	datamap_t *DataMapInit<className>( className * );                                                    \
	namespace className##_DataDescInit                                                                   \
	{                                                                                                    \
		datamap_t *g_DataMapHolder =                                                                     \
			DataMapInit( (className *)NULL ); /* This can/will be used for some clean up duties later */ \
	}                                                                                                    \
                                                                                                         \
	template <>                                                                                          \
	datamap_t *DataMapInit<className>( className * )                                                     \
	{                                                                                                    \
		typedef className classNameTypedef;                                                              \
		static CDatadescGeneratedNameHolder nameHolder( #className );                                    \
		className::m_DataMap.baseMap = className::GetBaseMap();                                          \
		static typedescription_t dataDesc[] = {                                                          \
			{ FIELD_VOID,                                                                                \
			  nullptr,                                                                                   \
			  { 0, 0 },                                                                                  \
			  0,                                                                                         \
			  0,                                                                                         \
			  nullptr,                                                                                   \
			  nullptr,                                                                                   \
			  nullptr,                                                                                   \
			  nullptr }, /* so you can define "empty" tables */

#define END_DATADESC()                                                                    \
	}                                                                                     \
	;                                                                                     \
                                                                                          \
	if ( sizeof( dataDesc ) > sizeof( dataDesc[0] ) )                                     \
	{                                                                                     \
		classNameTypedef::m_DataMap.dataNumFields = int( SIZE_OF_ARRAY( dataDesc ) - 1 ); \
		classNameTypedef::m_DataMap.dataDesc = &dataDesc[1];                              \
	}                                                                                     \
	else                                                                                  \
	{                                                                                     \
		classNameTypedef::m_DataMap.dataNumFields = 1;                                    \
		classNameTypedef::m_DataMap.dataDesc = dataDesc;                                  \
	}                                                                                     \
	return &classNameTypedef::m_DataMap;                                                  \
	}

// used for when there is no data description

// helps get the offset of a bitfield
#define BEGIN_BITFIELD( name ) \
	union                      \
	{                          \
		char name;             \
		struct                 \
		{
#define END_BITFIELD() \
	}                  \
	;                  \
	}                  \
	;

// Forward compatability with potential seperate byteswap datadescs

#define DECLARE_BYTESWAP_DATADESC() DECLARE_SIMPLE_DATADESC()
#define BEGIN_BYTESWAP_DATADESC( name ) BEGIN_SIMPLE_DATADESC( name )

#define END_BYTESWAP_DATADESC() END_DATADESC()

template <typename T>
inline void DataMapAccess( T *ignored, datamap_t **p )
{
	*p = &T::m_DataMap;
}

class CDatadescGeneratedNameHolder
{
public:
	CDatadescGeneratedNameHolder( const char *pszBase )
		: m_pszBase( pszBase )
	{
		m_nLenBase = strlen( m_pszBase );
	}

	~CDatadescGeneratedNameHolder()
	{
		for ( int i = 0; i < m_Names.Count(); i++ )
		{
			delete m_Names[i];
		}
	}

private:
	const char *m_pszBase;
	size_t m_nLenBase;
	CUtlVector<char *> m_Names;
};
