// Model precache without an entity.
#pragma once
#define PRECACHE_MODEL_H

// Add a model to the level precache list and return its model index, or -1.
int Precache_Model( const char *name, bool bPreload = true );

// Precache the sounds, particle systems and anim-event assets of an
// already-precached studio model. A no-op for a non-studio model.
void Precache_ModelComponents( int nModelIndex );
