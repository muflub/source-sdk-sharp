//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Generic CRC functions
#pragma once

typedef unsigned int CRC32_t;

void CRC32_Init( CRC32_t *pulCRC );
void CRC32_ProcessBuffer( CRC32_t *pulCRC, const void *p, int len );
void CRC32_Final( CRC32_t *pulCRC );
