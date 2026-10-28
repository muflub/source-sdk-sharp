//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include <vgui/MouseCode.h>

#include <vgui/Cursor.h>
#include <vgui/IImage.h>

namespace vgui
{

	// Purpose: basic interface for a HTML window
	class IHTML
	{
	public:
		// open a new page
		virtual void unused_OpenURL() = 0;

		// stops the existing page from loading
		virtual void unused_StopLoading() = 0;

		// refreshes the current page
		virtual bool Refresh()=0;

		// display the control -- deprecated !! Use SetVisible() instead!
		virtual void unused_Show() = 0;

		// return the currently opened page
		virtual void unused_GetOpenedPage() = 0;

		// called when the browser needs to be resized
		virtual void unused_Obsolete_OnSize() = 0;

		// returns the width and height (in pixels) of the HTML full page (not just the displayed region)
		virtual void unused_GetHTMLSize() = 0;

		// clear the text in an existing control
		virtual void Clear()=0;

		// add text to the browser control (as a HTML formated string)
		virtual void unused_AddText() = 0;

		enum MOUSE_STATE { UP,DOWN,MOVE,DBLCLICK };
		// unused functions we keep around so the vtable layout is binary compatible
		virtual void unused_Obsolete_OnMouse() = 0;
		virtual void unused_Obsolete_OnChar() = 0;
		virtual void unused_Obsolete_OnKeyDown() = 0;

		virtual void unused_GetBitmap() = 0;
		virtual void SetVisible( bool state ) = 0;

		virtual void SetSize( int wide,int tall )=0;

		virtual void unused_OnMouse() = 0;
		virtual void unused_OnChar() = 0;
		virtual void unused_OnKeyDown() = 0;

		virtual void unused_ScrollV() = 0;
		virtual void unused_ScrollH() = 0;
		virtual void unused_OnMouseWheeled() = 0;

		// called when the browser needs to be resized
		virtual void unused_OnKeyUp() = 0;

		// open a URL with the provided POST data (which can be much larger than the max URL of 512 chars)
		// NOTE - You CANNOT have get params (i.e a "?" ) in pchURL if pchPostData is set (due to an IE bug)
		virtual void unused_PostURL() = 0;

		// Run javascript within the browser control
		virtual void unused_RunJavascript() = 0;

		virtual void unused_SetMousePosition() = 0;

		virtual void unused_SetUserAgentInfo() = 0;

		// can't add custom headers to IE
		virtual void unused_AddHeader() = 0;	

		virtual void unused_SetFileDialogChoice() = 0;

		// we are hiding the popup, so make sure webkit knows
		virtual void unused_HidePopup() = 0;
		virtual void unused_SetHTMLFocus() = 0;
		virtual void unused_KillHTMLFocus() = 0;
		// ask webkit about the size of any scrollbars it wants to render
		virtual void unused_HorizontalScrollBarSize() = 0;
		virtual void unused_VerticalScrollBarSize() = 0;
		virtual void unused_HorizontalScroll() = 0;
		virtual void unused_VerticalScroll() = 0;
		virtual void unused_HorizontalScrollMax() = 0;
		virtual void unused_VerticalScrollMax() = 0;
		virtual void unused_IsHorizontalScrollBarVisible() = 0;
		virtual void unused_IsVeritcalScrollBarVisible() = 0;
		virtual void unused_SetHorizontalScroll() = 0;
		virtual void unused_SetVerticalScroll() = 0;
		virtual void unused_ViewSource() = 0;
		virtual void unused_Copy() = 0;
		virtual void unused_Paste() = 0;

		// IE specific calls
		virtual void unused_IsIERender() = 0;
		virtual void unused_GetIDispatchPtr() = 0;
		virtual void unused_GetHTMLScroll() = 0;
	};

	// Purpose: basic callback interface for a HTML window
	class IHTMLEvents
	{
	public:
		// unused functions we keep around so the vtable layout is binary compatible
		virtual void unused_Obsolete_OnStartURL() = 0;
		virtual void unused_Obsolete_OnFinishURL() = 0;
		virtual void unused_Obsolete_OnProgressURL() = 0;
		virtual void unused_Obsolete_OnSetStatusText() = 0;
		virtual void unused_Obsolete_OnUpdate() = 0;
		virtual void unused_Obsolete_OnLink() = 0;
		virtual void unused_Obsolete_OffLink() = 0;
	
		// call backs for events
		// when the top level browser is changing the page they are looking at (not sub iframes or the like loading)
		virtual void unused_OnURLChanged() = 0;
		// the control has finished loading a request, could be a sub request in the page
		virtual void unused_OnFinishRequest() = 0;

		// the lower html control wants to load a url, do we allow it?
		virtual void unused_OnStartRequestInternal() = 0;

		// show a popup menu for this html control
		virtual void unused_ShowPopup() = 0;
		// hide any popup menu you are showing
		virtual void unused_HidePopup() = 0;
		// show an external html window at this position and side
		virtual void unused_OnPopupHTMLWindow() = 0;
		// the browser is telling us the title it would like us to show
		virtual void unused_SetHTMLTitle() = 0;
		// the browser is loading a sub url for a page, usually an image or css
		virtual void unused_OnLoadingResource() = 0;
		// the browser is telling us the user is hovering a url or the like 
		virtual void unused_OnSetStatusText() = 0;
		// the browser wants the cursor changed please
		virtual void unused_OnSetCursor() = 0;
		// the browser wants to ask the user to select a local file and tell it about it
		virtual void unused_OnFileLoadDialog() = 0;
		// show and hide tooltip text
		virtual void unused_OnShowToolTip() = 0;
		virtual void unused_OnUpdateToolTip() = 0;
		virtual void unused_OnHideToolTip() = 0;

		// IE only code
		virtual void unused_BOnCreateNewWindow() = 0;
		virtual void unused_OnLink() = 0;
		virtual void unused_OffLink() = 0;
		virtual void unused_OnCloseWindow() = 0;
		virtual void unused_OnUpdate() = 0;
		virtual void unused_OnProgressRequest() = 0;

		// new Chrome calls
		virtual void unused_OnOpenNewTab() = 0;
	};

}
