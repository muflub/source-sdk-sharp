// Second-camera views drawn into render targets, from the client mode's PreRender.
#pragma once

// Draws every view queued by IShimViewClient::RenderViewToTarget this frame.
void ShellViewTargets_RenderQueued();

// A render target's texture, and a note that one more view was drawn into it.
class ITexture;
ITexture* ShellViewTargets_Texture(int nTarget);
void ShellViewTargets_NoteDrawn(int nTarget);

// While a view needs the viewer's body, SourceSharp_ThirdPersonSeesOwner answers
// true; End restores what Begin returned.
bool ShellClient_ViewerSeenBegin();
void ShellClient_ViewerSeenEnd(bool bWasSeen);
