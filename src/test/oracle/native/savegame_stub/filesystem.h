//========= SourceSharp — savegame_codec_oracle_test's stand-in ==============//
//
// The pristine game/shared/saverestore.h includes filesystem.h for one type,
// FileHandle_t (CSave's m_hLogFile). The oracle never logs, so the handle's
// declaration is all it needs, and the whole IFileSystem is not pulled in.
//
//=============================================================================//

#ifndef SAVEGAME_ORACLE_FILESYSTEM_H
#define SAVEGAME_ORACLE_FILESYSTEM_H

typedef void *FileHandle_t;

#endif // SAVEGAME_ORACLE_FILESYSTEM_H
