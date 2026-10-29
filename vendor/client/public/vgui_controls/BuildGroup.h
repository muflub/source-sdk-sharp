//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier1/utldict.h"

#include <vgui/Cursor.h>

#include "tier1/utlhandletable.h"

class KeyValues;

namespace vgui
{

// Purpose: a BuildGroup is a list of panels contained in a window (the contextPanel)
//			Members of this group are viewable and editable in Build Mode, via the BuildModeDialog wizard
class BuildGroup final
{
	DECLARE_HANDLES(BuildGroup, 20);

public:
	BuildGroup(Panel* parentPanel, Panel* contextPanel);
	~BuildGroup();

protected:
private:
	bool _enabled;
	int _snapX;
	int _snapY;
	HCursor _cursor_sizenwse;
	HCursor _cursor_sizenesw;
	HCursor _cursor_sizewe;
	HCursor _cursor_sizens;
	HCursor _cursor_sizeall;
	bool _dragging;
	MouseCode _dragMouseCode;
	int _dragStartPanelPos[2];
	int _dragStartCursorPos[2];
	int _dragStartPanelSize[2];
	Panel* _currentPanel;
	CUtlVector<PHandle> _panelDar;
	char* m_pResourceName;
	char* m_pResourcePathID;
	PHandle m_hBuildDialog;
	Panel* m_pBuildContext; // the panel from which the build dialog gets all the information it needs
	Panel* m_pParentPanel; // panel to create new controls in
	CUtlVector<PHandle> _controlGroup; // grouped panels
	CUtlVector<int> _groupDeltaX; // x offsets of panels in group from the selected panel
	CUtlVector<int> _groupDeltaY; // y offsets of panels in group from the selected panel
	Label* _rulerNumber[4]; // 4 numbers to label rulers with
	bool _showRulers; // toggles ruler display
	CUtlVector<CUtlSymbol> m_RegisteredControlSettingsFiles;

	friend class Panel;
};

// Handle to a build group
typedef CUtlHandle<BuildGroup> HBuildGroup;

} // namespace vgui
