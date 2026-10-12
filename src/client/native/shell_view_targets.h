//========= SourceSharp -- a second camera, drawn into a render target ========//
//
// shell_view_targets.cpp's two seams into the rest of the shell (lane
// camproto). The draw runs from the client mode's PreRender; the viewer's body
// is the client mode's to show, because the flag that hides it in first person
// lives beside the third-person camera that sets it (shell_client.cpp).
//
//=============================================================================//
#ifndef SHELL_VIEW_TARGETS_H
#define SHELL_VIEW_TARGETS_H

// Draws every view queued by IShimViewClient::RenderViewToTarget this frame.
void ShellViewTargets_RenderQueued();

// The render plan's second-camera pass (C7 item 6): a target's texture, and
// one more view drawn into it.
class ITexture;
ITexture *ShellViewTargets_Texture( int nTarget );
void ShellViewTargets_NoteDrawn( int nTarget );

// For a view whose setup asks for the viewer's body: SourceSharp_ThirdPersonSeesOwner
// answers true and the body joins the leaf system until the matching End, which
// restores what Begin returned.
bool ShellClient_ViewerSeenBegin();
void ShellClient_ViewerSeenEnd( bool bWasSeen );

#endif // SHELL_VIEW_TARGETS_H
