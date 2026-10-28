//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Helper classes and functions for the save/restore system. These
// 			classes are internally structured to distinguish simple from
//			complex types.

#pragma once

#include "isaverestore.h"
#include "utlvector.h"
#include "filesystem.h"

class CSaveRestoreData;
class CSaveRestoreSegment;
class CGameSaveRestoreInfo;
struct typedescription_t;
struct edict_t;
struct datamap_t;
class CBaseEntity;
struct interval_t;

// CSave

class CSave : public ISave
{
public:
	CSave( CSaveRestoreData *pdata );
	
	// Logging
	void			StartLogging( const char *pszLogName );
	void			EndLogging( void );

	bool			IsAsync();

	int				GetWritePos() const;
	void			SetWritePos(int pos);

	// Datamap based writing
	
	int				WriteAll( const void *pLeafObject, datamap_t *pLeafMap )	{ return DoWriteAll( pLeafObject, pLeafMap, pLeafMap ); }
	
	int				WriteFields( const char *pname, const void *pBaseData, datamap_t *pMap, typedescription_t *pFields, int fieldCount );

	// Block support
	
	virtual void	StartBlock( const char *pszBlockName );
	virtual void	StartBlock();
	virtual void	EndBlock();
	
	// Primitive types
	
	void			WriteShort( const short *value, int count = 1 );
	void			WriteInt( const int *value, int count = 1 );		                           // Save an int
	void			WriteBool( const bool *value, int count = 1 );		                           // Save a bool
	void			WriteFloat( const float *value, int count = 1 );	                           // Save a float
	void			WriteData( const char *pdata, int size );		                               // Save a binary data block
	void			WriteString( const char *pstring );			                                   // Save a null-terminated string
	void			WriteString( const string_t *stringId, int count = 1 );	                       // Save a null-terminated string (engine string)
	void			WriteVector( const Vector &value );				                               // Save a vector
	void			WriteVector( const Vector *value, int count = 1 );	                           // Save a vector array
	void			WriteQuaternion( const Quaternion &value );				                        // Save a Quaternion
	void			WriteQuaternion( const Quaternion *value, int count = 1 );	                    // Save a Quaternion array

	// Note: All of the following will write out both a header and the data. On restore,
	// this needs to be cracked
	void			WriteShort( const char *pname, const short *value, int count = 1 );
	void			WriteInt( const char *pname, const int *value, int count = 1 );		           // Save an int
	void			WriteBool( const char *pname, const bool *value, int count = 1 );		       // Save a bool
	void			WriteFloat( const char *pname, const float *value, int count = 1 );	           // Save a float
	void			WriteData( const char *pname, int size, const char *pdata );		           // Save a binary data block
	void			WriteString( const char *pname, const char *pstring );			               // Save a null-terminated string
	void			WriteString( const char *pname, const string_t *stringId, int count = 1 );	   // Save a null-terminated string (engine string)
	void			WriteVector( const char *pname, const Vector &value );				           // Save a vector
	void			WriteVector( const char *pname, const Vector *value, int count = 1 );	       // Save a vector array
	void			WriteQuaternion( const char *pname, const Quaternion &value );				   // Save a Quaternion
	void			WriteQuaternion( const char *pname, const Quaternion *value, int count = 1 );  // Save a Quaternion array
	// Game types
	
	void			WriteTime( const char *pname, const float *value, int count = 1 );	           // Save a float (timevalue)
	void			WriteTick( const char *pname, const int *value, int count = 1 );	           // Save a int (timevalue)
	void			WritePositionVector( const char *pname, const Vector &value );		           // Offset for landmark if necessary
	void			WritePositionVector( const char *pname, const Vector *value, int count = 1 );  // array of pos vectors
	void			WriteFunction( datamap_t *pMap, const char *pname, inputfunc_t **value, int count = 1 ); // Save a function pointer

	void			WriteEntityPtr( const char *pname, CBaseEntity **ppEntity, int count = 1 );
	void			WriteEdictPtr( const char *pname, edict_t **ppEdict, int count = 1 );
	void			WriteEHandle( const char *pname, const EHANDLE *pEHandle, int count = 1 );

	virtual void	WriteTime( const float *value, int count = 1 );	// Save a float (timevalue)
	virtual void	WriteTick( const int *value, int count = 1 );	// Save a int (timevalue)
	virtual void	WritePositionVector( const Vector &value );		// Offset for landmark if necessary
	virtual void	WritePositionVector( const Vector *value, int count = 1 );	// array of pos vectors

	virtual void	WriteEntityPtr( CBaseEntity **ppEntity, int count = 1 );
	virtual void	WriteEdictPtr( edict_t **ppEdict, int count = 1 );
	virtual void	WriteEHandle( const EHANDLE *pEHandle, int count = 1 );

	CGameSaveRestoreInfo *GetGameSaveRestoreInfo()	{ return m_pGameInfo; }

private:

	void			Log( const char *pName, fieldtype_t fieldType, void *value, int count );

	int				DoWriteAll( const void *pLeafObject, datamap_t *pLeafMap, datamap_t *pCurMap );
	
	// Game info methods
	
	alignas( 8 ) unsigned char unused_m_BlockStartStack[ 40 ];
	
	// Game data
	CGameSaveRestoreInfo *m_pGameInfo;

	FileHandle_t		m_hLogFile;
	bool				m_bAsync;
};

// CRestore

class CRestore : public IRestore
{
public:
	CRestore( CSaveRestoreData *pdata );
	
	int				GetReadPos() const;
	void			SetReadPos( int pos );

	// Datamap based reading
	
	int				ReadAll( void *pLeafObject, datamap_t *pLeafMap )	{ return DoReadAll( pLeafObject, pLeafMap, pLeafMap ); }
	
	int				ReadFields( const char *pname, void *pBaseData, datamap_t *pMap, typedescription_t *pFields, int fieldCount );
	void 			EmptyFields( void *pBaseData, typedescription_t *pFields, int fieldCount );

	// Block support
	
	virtual void	StartBlock( SaveRestoreRecordHeader_t *pHeader );
	virtual void	StartBlock( char szBlockName[SIZE_BLOCK_NAME_BUF] );
	virtual void	StartBlock();
	virtual void	EndBlock();
	
	// Field header cracking
	
	void			ReadHeader( SaveRestoreRecordHeader_t *pheader );
	int				SkipHeader()	{ SaveRestoreRecordHeader_t header; ReadHeader( &header ); return header.size; }
	const char *	StringFromHeaderSymbol( int symbol );

	// Primitive types
	
	short			ReadShort( void );
	int				ReadShort( short *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadInt( int *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadInt( void );
	int 			ReadBool( bool *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadFloat( float *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadData( char *pData, int size, int nBytesAvailable );
	void			ReadString( char *pDest, int nSizeDest, int nBytesAvailable );			// A null-terminated string
	int				ReadString( string_t *pString, int count = 1, int nBytesAvailable = 0);
	int				ReadVector( Vector *pValue );
	int				ReadVector( Vector *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadQuaternion( Quaternion *pValue );
	int				ReadQuaternion( Quaternion *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadVMatrix( VMatrix *pValue, int count = 1, int nBytesAvailable = 0);
	
	// Game types
	
	int				ReadTime( float *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadTick( int *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadPositionVector( Vector *pValue );
	int				ReadPositionVector( Vector *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadFunction( datamap_t *pMap, inputfunc_t **pValue, int count = 1, int nBytesAvailable = 0);
	
	int 			ReadEntityPtr( CBaseEntity **ppEntity, int count = 1, int nBytesAvailable = 0 );
	int				ReadEdictPtr( edict_t **ppEdict, int count = 1, int nBytesAvailable = 0 );
	int				ReadEHandle( EHANDLE *pEHandle, int count = 1, int nBytesAvailable = 0 );
	int				ReadVMatrixWorldspace( VMatrix *pValue, int count = 1, int nBytesAvailable = 0);
	int				ReadMatrix3x4Worldspace( matrix3x4_t *pValue, int nElems = 1, int nBytesAvailable = 0 );
	
	bool			GetPrecacheMode( void ) { return m_precache; }

	CGameSaveRestoreInfo *GetGameSaveRestoreInfo()	{ return m_pGameInfo; }

private:
	// Read primitives
	
	int				DoReadAll( void *pLeafObject, datamap_t *pLeafMap, datamap_t *pCurMap );
	
	alignas( 8 ) unsigned char unused_m_BlockEndStack[ 40 ];

	// Game data
	CGameSaveRestoreInfo *	m_pGameInfo;
	alignas( 8 ) unsigned char unused_m_global[ 4 ];
	bool					m_precache;
};
