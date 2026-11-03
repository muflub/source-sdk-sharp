/*
 * Orbiting, panning, zooming, picking: what the mouse means to a View.
 *
 * On the container rather than per canvas, and the move and release are on the
 * window: a drag that leaves the canvas it started in should keep going, which
 * is exactly what happens when someone spins a camera quickly.
 */

import type { Drag, View } from './renderer';

/**
 * Wires a container's mouse to the views in it. A press that did not move is a
 * click, handed over at its position in the canvas. Answers how to undo it.
 */
export function attachCamera(
  element: HTMLElement,
  viewFor: (target: EventTarget | null) => View | null,
  draw: () => void,
  onClick: (view: View, x: number, y: number) => void,
): () => void {
  let dragging: { view: View; drag: Drag } | null = null;

  const onMouseDown = (event: MouseEvent): void => {
    const view = viewFor(event.target);
    if (!view) return;

    dragging = { view, drag: view.beginDrag(event) };
    event.preventDefault();
  };

  const onMouseMove = (event: MouseEvent): void => {
    if (!dragging) return;

    dragging.view.applyDrag(dragging.drag,
      event.clientX - dragging.drag.x, event.clientY - dragging.drag.y);

    draw();
  };

  const onMouseUp = (event: MouseEvent): void => {
    if (dragging && !dragging.drag.moved && event.button === 0) {
      const rect = dragging.view.canvas.getBoundingClientRect();
      onClick(dragging.view, event.clientX - rect.left, event.clientY - rect.top);
    }

    dragging = null;
  };

  const onWheel = (event: WheelEvent): void => {
    const view = viewFor(event.target);
    if (!view) return;

    event.preventDefault();
    view.zoomBy(event.deltaY);
    draw();
  };

  const noMenu = (event: Event): void => event.preventDefault();

  element.addEventListener('mousedown', onMouseDown);
  element.addEventListener('contextmenu', noMenu);

  // Not passive, because zooming the map must not also scroll the page.
  element.addEventListener('wheel', onWheel, { passive: false });
  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mouseup', onMouseUp);

  return () => {
    element.removeEventListener('mousedown', onMouseDown);
    element.removeEventListener('contextmenu', noMenu);
    element.removeEventListener('wheel', onWheel);
    window.removeEventListener('mousemove', onMouseMove);
    window.removeEventListener('mouseup', onMouseUp);
  };
}
