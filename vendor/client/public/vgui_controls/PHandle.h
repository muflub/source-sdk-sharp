//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

namespace vgui
{

class Panel;

// Purpose: Safe pointer class for handling Panel or derived panel classes
class PHandle
{
public:
	PHandle()
		: m_iPanelID( INVALID_PANEL )
	{
	} //m_iSerialNumber(0), m_pListEntry(0) {}

	Panel *Get() const;
	Panel *Set( Panel *pPanel );
	Panel *Set( HPanel hPanel );

	operator Panel *() const { return Get(); }

	operator bool() { return Get() != 0; }

private:
	HPanel m_iPanelID;
};

// Purpose: Safe pointer class to just convert between VPANEL's and PHandle
class VPanelHandle
{
public:
	VPanelHandle()
		: m_iPanelID( INVALID_PANEL )
	{
	}

	VPANEL Get() const;
	VPANEL Set( VPANEL pPanel );

	operator VPANEL() const { return Get(); }

	operator bool() const { return Get() != 0; }

private:
	HPanel m_iPanelID;
};

// Purpose: DHANDLE is a templated version of PHandle
template <class PanelType>
class DHANDLE : public PHandle
{
public:
	PanelType *Get() const { return (PanelType *)PHandle::Get(); }

	PanelType *Set( PanelType *pPanel ) { return (PanelType *)PHandle::Set( pPanel ); }

	PanelType *Set( HPanel hPanel ) { return (PanelType *)PHandle::Set( hPanel ); }

	operator PanelType *() const { return (PanelType *)PHandle::Get(); }

	operator bool() const { return PHandle::Get() != NULL; }
};

};
