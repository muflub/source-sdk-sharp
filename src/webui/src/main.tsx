/*
 * Where the page starts.
 *
 * The stylesheet is imported here rather than linked from the HTML so that the
 * bundler owns it: one build step produces app.js and app.css together, and
 * neither can be stale against the other.
 */

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import './app.css';
import { App } from './App';

const container = document.getElementById('root');

if (!container) {
  throw new Error('no #root to render into');
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
