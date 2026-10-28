//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class CHudTexture;

class CHudTextureHandle
{
public:
	CHudTextureHandle()
	{
		m_pValue = NULL;
	}

	void Set( CHudTexture *t )
	{
		m_pValue = t;
	}

	CHudTexture *Get()
	{
		return m_pValue;
	}

	operator CHudTexture*()
	{
		return m_pValue;
	}

	operator CHudTexture*() const
	{
		return m_pValue;
	}

private:
	CHudTexture *m_pValue;
};
