/*
 * A clean address bar for every test.
 *
 * The page keeps what you set up to look at — which game, which pane, what the
 * list is filtered to — in the URL, so that a view can be sent to somebody as a
 * link. Tests share one jsdom document and therefore one address, and without
 * this a test that types into a filter box leaves that filter applied for
 * whatever runs next.
 *
 * And a findBy or waitFor waits up to 4 s, not Testing Library's 1 s: under
 * make check's concurrent legs a file's first render took longer than a second
 * (Scripted.test.tsx's first heading, on a CI runner), which is load, not a
 * missing element. It stays under vitest's 5 s, so a test that waits for
 * something that never comes still fails as itself.
 */

import { configure } from '@testing-library/react';
import { beforeEach } from 'vitest';

configure({ asyncUtilTimeout: 4000 });

beforeEach(() => history.replaceState(null, '', '/'));
