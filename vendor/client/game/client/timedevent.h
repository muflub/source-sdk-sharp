//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// This class triggers events at a specified rate. Just call NextEvent() and do an event until it 
// returns false. For example, if you want to spawn particles 10 times per second, do this:
// pTimer->SetRate(10);
// float tempDelta = fTimeDelta;
// while(pTimer->NextEvent(tempDelta))
//		spawn a particle

class TimedEvent
{
public:
				TimedEvent()
				{
					m_TimeBetweenEvents = -1;
					m_fNextEvent = 0;
				}

	// Rate is in events per second (ie: rate of 15 will trigger 15 events per second).
	inline void	Init(float rate)			
	{
		m_TimeBetweenEvents = 1.0f / rate;
		m_fNextEvent = 0;
	}
	
private:
	float		m_TimeBetweenEvents;
	float		m_fNextEvent;	// When the next event should be triggered.
};
