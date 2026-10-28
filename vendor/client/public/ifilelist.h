//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// This class represents a group of files. Internally, it can represent whole folders of files
// that are in or out of the group. So you can't iterate the list, but you can ask the
// class if a particular filename is in the list.
class IFileList
{
public:
	virtual void unused_IsFileInList() = 0;
	virtual void Release() = 0;
};
