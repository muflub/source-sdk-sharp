// What the input code asks about the local viewer, as free functions. CViewerFacts
// (shell_viewer_facts.h) answers the rest.
#pragma once

// Whether the last frame's camera can see the viewer's own body: where the
// camera ended up, not the third-person mode that was typed. Defined in
// shell_client.cpp; also declared in shell_standalone_client.h.
bool SourceSharp_ThirdPersonSeesOwner();

// Whether the viewer's aim is being assisted onto a target. Always false: the
// HL2 autoaim state is not networked to the managed player.
bool SourceSharp_ViewerAimIsAssisted();
